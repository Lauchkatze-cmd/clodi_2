using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Moves Clodi like a heavy little animal instead of a spinning top:
/// - smooth acceleration and braking, limited turn speed, always walks where he looks
/// - SPRINT ability (hold Shift / left stick click): faster, uses stamina that refills over time
/// - AIMED mud ball (left click): throws towards the mouse cursor; if an enemy is close to the cursor
///   (or in front of Clodi) the ball locks on and flies in an arc that hits the moving enemy
/// - the walk animation plays faster/slower to match the real speed (also while sprinting)
///   and keeps stepping while Clodi turns on the spot, so he never "slides" around
/// Needs: Rigidbody + Collider on the same GameObject, Animator on the model (child).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Input (drag the actions from InputSystem_Actions here)")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference attackAction;
    [Tooltip("Optional. If empty, Left Shift (keyboard) and left stick click (gamepad) are used.")]
    [SerializeField] private InputActionReference sprintAction;

    [Header("Movement")]
    [Tooltip("Normal walking speed in meters per second.")]
    [SerializeField] private float walkSpeed = 3f;
    [Tooltip("How fast Clodi speeds up (m/s per second).")]
    [SerializeField] private float acceleration = 8f;
    [Tooltip("How fast Clodi slows down when you let go (m/s per second).")]
    [SerializeField] private float deceleration = 12f;
    [Tooltip("Maximum turning speed in degrees per second.")]
    [SerializeField] private float turnSpeedDegrees = 220f;
    [Tooltip("Slower turning while walking fast feels more natural.")]
    [SerializeField] private float turnSpeedAtFullSpeed = 140f;
    [Tooltip("Above this angle (degrees) between facing and input, Clodi turns on the spot first.")]
    [SerializeField] private float turnInPlaceAngle = 100f;
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Sprint Ability")]
    [Tooltip("Sprint speed = walk speed x this value.")]
    [SerializeField] private float sprintMultiplier = 1.8f;
    [Tooltip("How many seconds Clodi can sprint with full stamina.")]
    [SerializeField] private float maxStamina = 3f;
    [Tooltip("Stamina refilled per second.")]
    [SerializeField] private float staminaRegen = 0.8f;
    [Tooltip("Wait this long after sprinting before stamina refills.")]
    [SerializeField] private float staminaRegenDelay = 0.8f;
    [Tooltip("After running out of stamina, Clodi can only sprint again when this much (0-1) is back.")]
    [SerializeField] private float staminaNeededToRestart = 0.35f;
    [Tooltip("Sprinting turns slower than walking.")]
    [SerializeField] private float sprintTurnMultiplier = 0.7f;

    [Header("Mud Ball Attack")]
    [SerializeField] private GameObject mudBallPrefab;
    [SerializeField] private Transform throwPoint;
    [Tooltip("Horizontal flight speed of the mud ball (m/s). The arc is calculated from it.")]
    [SerializeField] private float throwSpeed = 12f;
    [SerializeField] private float maxThrowRange = 16f;
    [SerializeField] private float attackCooldown = 0.8f;
    [Tooltip("Enemies closer than this to the mouse cursor are locked on.")]
    [SerializeField] private float cursorLockRadius = 3f;
    [Tooltip("Without mouse aim: enemies inside this cone in front of Clodi are locked on (degrees).")]
    [SerializeField] private float autoAimAngle = 30f;
    [Tooltip("Aim with the mouse cursor. Turn off to always throw forward (with auto aim).")]
    [SerializeField] private bool useMouseAim = true;
    [Tooltip("How fast Clodi turns towards the target before throwing (degrees per second).")]
    [SerializeField] private float aimTurnSpeed = 720f;
    [SerializeField] private bool showAimMarker = true;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [Tooltip("Use the walk animation from the Animator (on) or only the code waddle in ModelPoseLock (off).")]
    [SerializeField] private bool useSwimAnimationForWalking = true;
    [Tooltip("Speed (m/s) at which the walk animation looks right at normal playback speed. " +
             "If the feet slide forward, lower it; if they slide backward, raise it.")]
    [SerializeField] private float animationReferenceSpeed = 3f;
    [SerializeField] private float minAnimationSpeed = 0.6f;
    [SerializeField] private float maxAnimationSpeed = 2.2f;
    [Tooltip("Animation speed while turning on the spot (small steps).")]
    [SerializeField] private float turnStepAnimationSpeed = 0.8f;

    private Rigidbody rb;
    private Collider bodyCollider;
    private Camera mainCamera;
    private Vector2 moveInput;
    private bool jumpRequested;
    private float nextAttackTime;
    private float currentSpeed;
    private bool isAttacking;
    private float yawRate;
    private float lastYaw;

    private float stamina;
    private float lastSprintTime = -10f;
    private bool sprintLocked;
    private bool isSprinting;

    private EnemyStatus[] enemies = new EnemyStatus[0];
    private float nextEnemySearch;
    private Transform aimMarker;
    private Renderer aimMarkerRenderer;
    private EnemyStatus lockedTarget;
    private Vector3 aimPoint;

    public float CurrentSpeed => currentSpeed;
    public float Stamina01 => maxStamina > 0f ? stamina / maxStamina : 0f;
    public bool IsSprinting => isSprinting;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // physics must never tip Clodi over
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        bodyCollider = GetComponent<Collider>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
        mainCamera = Camera.main;
        stamina = maxStamina;
        lastYaw = transform.eulerAngles.y;

        if (GetComponent<StaminaBar>() == null) gameObject.AddComponent<StaminaBar>();
        if (showAimMarker) CreateAimMarker();
    }

    private void OnEnable()
    {
        if (moveAction == null || jumpAction == null || attackAction == null)
        {
            Debug.LogError("PlayerController: Please assign Move, Jump and Attack actions in the Inspector.", this);
            enabled = false;
            return;
        }

        moveAction.action.Enable();
        jumpAction.action.Enable();
        attackAction.action.Enable();
        if (sprintAction != null) sprintAction.action.Enable();
        jumpAction.action.performed += OnJump;
        attackAction.action.performed += OnAttack;
    }

    private void OnDisable()
    {
        if (jumpAction != null) jumpAction.action.performed -= OnJump;
        if (attackAction != null) attackAction.action.performed -= OnAttack;
    }

    private void Update()
    {
        bool playing = GameManager.Instance == null || GameManager.Instance.IsPlaying;
        moveInput = playing ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;

        UpdateSprint(playing);
        UpdateAim(playing);
        UpdateAnimation();
    }

    // ------------------------------------------------------------------
    // Movement
    // ------------------------------------------------------------------

    private void FixedUpdate()
    {
        Vector3 wishDirection = GetCameraRelativeDirection(moveInput);
        float inputStrength = Mathf.Clamp01(wishDirection.magnitude);
        float maxSpeed = walkSpeed * (isSprinting ? sprintMultiplier : 1f);

        // Turning: limited degrees per second (slower at high speed and while sprinting)
        float angleToInput = 0f;
        if (inputStrength > 0.05f && !isAttacking)
        {
            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;
            angleToInput = Vector3.Angle(flatForward, wishDirection);

            float speedPercent = maxSpeed > 0f ? Mathf.Clamp01(currentSpeed / walkSpeed) : 0f;
            float turn = Mathf.Lerp(turnSpeedDegrees, turnSpeedAtFullSpeed, speedPercent);
            if (isSprinting) turn *= sprintTurnMultiplier;
            Quaternion target = Quaternion.LookRotation(wishDirection.normalized, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, target, turn * Time.fixedDeltaTime));
        }

        // Speed: walk slower while the input points away from where Clodi looks (turn first, then walk)
        float alignment = angleToInput >= turnInPlaceAngle ? 0f : Mathf.Cos(angleToInput * Mathf.Deg2Rad);
        float targetSpeed = maxSpeed * inputStrength * Mathf.Clamp01(alignment);
        if (isAttacking) targetSpeed *= 0.2f;

        float rate = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);

        // Always move along the facing direction, never sideways
        Vector3 forward = rb.rotation * Vector3.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 velocity = forward * currentSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        // Turn rate (degrees/second) for the step animation while turning on the spot
        float yaw = rb.rotation.eulerAngles.y;
        yawRate = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / Time.fixedDeltaTime;
        lastYaw = yaw;

        if (jumpRequested)
        {
            jumpRequested = false;
            if (IsGrounded()) rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        if (mainCamera == null) return Vector3.ClampMagnitude(new Vector3(input.x, 0f, input.y), 1f);

        Vector3 forward = mainCamera.transform.forward;
        Vector3 right = mainCamera.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();
        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    private bool IsGrounded()
    {
        if (bodyCollider == null) return true;
        Bounds bounds = bodyCollider.bounds;
        return Physics.Raycast(bounds.center, Vector3.down, bounds.extents.y + groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
        jumpRequested = true;
    }

    // ------------------------------------------------------------------
    // Sprint ability
    // ------------------------------------------------------------------

    private bool SprintButtonHeld()
    {
        if (sprintAction != null) return sprintAction.action.IsPressed();
        bool keyboard = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        bool gamepad = Gamepad.current != null && Gamepad.current.leftStickButton.isPressed;
        return keyboard || gamepad;
    }

    private void UpdateSprint(bool playing)
    {
        bool wantsSprint = playing && SprintButtonHeld() && moveInput.sqrMagnitude > 0.1f && !isAttacking;

        if (sprintLocked && Stamina01 >= staminaNeededToRestart) sprintLocked = false;
        isSprinting = wantsSprint && !sprintLocked && stamina > 0f;

        if (isSprinting)
        {
            stamina = Mathf.Max(0f, stamina - Time.deltaTime);
            lastSprintTime = Time.time;
            if (stamina <= 0f) sprintLocked = true; // out of breath
        }
        else if (Time.time > lastSprintTime + staminaRegenDelay)
        {
            stamina = Mathf.Min(maxStamina, stamina + staminaRegen * Time.deltaTime);
        }
    }

    // ------------------------------------------------------------------
    // Aiming and mud ball
    // ------------------------------------------------------------------

    private void UpdateAim(bool playing)
    {
        if (Time.time >= nextEnemySearch)
        {
            nextEnemySearch = Time.time + 0.5f;
            enemies = FindObjectsByType<EnemyStatus>(FindObjectsSortMode.None);
        }

        aimPoint = GetMouseGroundPoint(out bool hasMousePoint);
        lockedTarget = FindTarget(hasMousePoint);
        if (lockedTarget != null) aimPoint = lockedTarget.transform.position;

        if (aimMarker != null)
        {
            bool visible = playing && (hasMousePoint || lockedTarget != null);
            aimMarker.gameObject.SetActive(visible);
            if (visible)
            {
                aimMarker.position = aimPoint + Vector3.up * 0.05f;
                float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.08f;
                float size = lockedTarget != null ? 1.4f : 0.7f;
                aimMarker.localScale = new Vector3(size * pulse, 0.01f, size * pulse);
                aimMarkerRenderer.material.color = lockedTarget != null
                    ? new Color(1f, 0.35f, 0.2f, 0.8f)   // locked on: red-orange
                    : new Color(1f, 1f, 1f, 0.5f);       // free aim: white
            }
        }
    }

    private Vector3 GetMouseGroundPoint(out bool found)
    {
        found = false;
        if (!useMouseAim || mainCamera == null || Mouse.current == null) return transform.position + transform.forward * 6f;

        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, groundLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.attachedRigidbody != rb)
        {
            Vector3 point = hit.point;
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.magnitude > maxThrowRange) point = transform.position + flat.normalized * maxThrowRange + Vector3.up * (point.y - transform.position.y);
            found = true;
            return point;
        }
        return transform.position + transform.forward * 6f;
    }

    private EnemyStatus FindTarget(bool hasMousePoint)
    {
        EnemyStatus best = null;
        float bestScore = float.MaxValue;
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;

        foreach (EnemyStatus enemy in enemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled) continue;
            Vector3 toEnemy = enemy.transform.position - transform.position;
            toEnemy.y = 0f;
            float distance = toEnemy.magnitude;
            if (distance > maxThrowRange) continue;

            float score;
            if (hasMousePoint)
            {
                Vector3 toCursor = enemy.transform.position - aimPoint;
                toCursor.y = 0f;
                score = toCursor.magnitude;
                if (score > cursorLockRadius) continue;
            }
            else
            {
                float angle = Vector3.Angle(flatForward, toEnemy);
                if (angle > autoAimAngle) continue;
                score = angle + distance * 0.5f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }
        return best;
    }

    private void OnAttack(InputAction.CallbackContext context)
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
        if (Time.time < nextAttackTime || isAttacking) return;
        nextAttackTime = Time.time + attackCooldown;
        StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        isAttacking = true;
        isSprinting = false;

        // 1. Turn quickly towards the target (max 0.25 s), so the throw comes out of Clodi's mouth
        Vector3 targetPoint = lockedTarget != null ? lockedTarget.transform.position : aimPoint;
        float turnTime = 0f;
        while (turnTime < 0.25f)
        {
            Vector3 direction = targetPoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) break;
            Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, look, aimTurnSpeed * Time.deltaTime));
            if (Quaternion.Angle(rb.rotation, look) < 3f) break;
            turnTime += Time.deltaTime;
            yield return null;
        }

        // 2. Play the attack animation and throw at the right moment of it
        if (animator != null) animator.SetTrigger("Action");
        yield return new WaitForSeconds(0.15f);
        ThrowMudBall();

        yield return new WaitForSeconds(0.3f);
        isAttacking = false;
    }

    private void ThrowMudBall()
    {
        if (mudBallPrefab == null) return;

        Vector3 spawnPosition = throwPoint != null
            ? throwPoint.position
            : transform.position + Vector3.up * 0.6f + transform.forward * 0.9f;
        GameObject ball = Instantiate(mudBallPrefab, spawnPosition, Quaternion.identity);

        if (bodyCollider != null && ball.TryGetComponent(out Collider ballCollider))
        {
            Physics.IgnoreCollision(ballCollider, bodyCollider);
        }

        if (!ball.TryGetComponent(out Rigidbody ballBody)) return;

        // Where should the ball land? Enemy: middle of the body, and lead the target if it moves
        Vector3 target;
        if (lockedTarget != null)
        {
            target = lockedTarget.transform.position + Vector3.up * 1f;
            Vector3 enemyVelocity = Vector3.zero;
            if (lockedTarget.TryGetComponent(out NavMeshAgent agent)) enemyVelocity = agent.velocity;
            float roughTime = Vector3.Distance(spawnPosition, target) / throwSpeed;
            target += enemyVelocity * roughTime;
        }
        else
        {
            target = aimPoint + Vector3.up * 0.3f;
        }

        // Ballistic arc: v = (distance - 0.5 * g * t^2) / t
        Vector3 delta = target - spawnPosition;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float time = Mathf.Clamp(flat.magnitude / throwSpeed, 0.2f, 1.5f);
        Vector3 launch = (delta - 0.5f * Physics.gravity * time * time) / time;

        ballBody.useGravity = true;
        ballBody.linearVelocity = launch;
    }

    private void CreateAimMarker()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "AimMarker";
        Destroy(marker.GetComponent<Collider>());
        aimMarkerRenderer = marker.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null) aimMarkerRenderer.material = new Material(shader);
        aimMarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimMarker = marker.transform;
        marker.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Animation
    // ------------------------------------------------------------------

    private void UpdateAnimation()
    {
        if (animator == null) return;

        bool moving = currentSpeed > 0.15f;
        bool turningOnSpot = !moving && yawRate > 30f;
        bool walking = (moving || turningOnSpot) && useSwimAnimationForWalking;
        animator.SetBool("isWalking", walking);

        // Match the step rhythm to the real speed so the feet don't slide
        if (walking && moving && animationReferenceSpeed > 0f)
        {
            animator.speed = Mathf.Clamp(currentSpeed / animationReferenceSpeed, minAnimationSpeed, maxAnimationSpeed);
        }
        else if (walking && turningOnSpot)
        {
            animator.speed = turnStepAnimationSpeed; // small steps while turning
        }
        else
        {
            animator.speed = 1f;
        }
    }
}
