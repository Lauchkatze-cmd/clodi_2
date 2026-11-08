using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Moves Clodi with the new Input System, makes him jump and throw mud balls.
/// Needs: Rigidbody + Collider on the same GameObject, Animator on the model (child).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Input (drag the actions from InputSystem_Actions here)")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference attackAction;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float turnSpeed = 10f;
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Mud Ball Attack")]
    [SerializeField] private GameObject mudBallPrefab;
    [SerializeField] private Transform throwPoint;
    [SerializeField] private float throwForce = 12f;
    [SerializeField] private float attackCooldown = 0.8f;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    private Rigidbody rb;
    private Collider bodyCollider;
    private Transform cameraTransform;
    private Vector2 moveInput;
    private bool jumpRequested;
    private float nextAttackTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // Clodi must not fall over
        rb.interpolation = RigidbodyInterpolation.Interpolate; // smoother camera follow
        bodyCollider = GetComponent<Collider>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false; // the script moves Clodi, not the animation
        if (Camera.main != null) cameraTransform = Camera.main.transform;
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
        // Stop the player while the game is paused or over
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
        {
            moveInput = Vector2.zero;
            SetWalking(false);
            return;
        }

        moveInput = moveAction.action.ReadValue<Vector2>();
        SetWalking(moveInput.sqrMagnitude > 0.01f);
    }

    private void FixedUpdate()
    {
        Vector3 direction = GetCameraRelativeDirection(moveInput);

        // Move on the XZ plane, keep the vertical velocity from gravity / jumping
        Vector3 velocity = direction * moveSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        // Turn towards the walking direction
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }

        if (jumpRequested)
        {
            jumpRequested = false;
            if (IsGrounded())
            {
                rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
        }
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        if (cameraTransform == null) return new Vector3(input.x, 0f, input.y);

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 direction = forward * input.y + right * input.x;
        return Vector3.ClampMagnitude(direction, 1f);
    }

    private bool IsGrounded()
    {
        // Cast from the middle of the collider down to just below its bottom,
        // so it works no matter where the model's pivot is
        if (bodyCollider == null) return true;
        Bounds bounds = bodyCollider.bounds;
        return Physics.Raycast(bounds.center, Vector3.down, bounds.extents.y + groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
        jumpRequested = true;
    }

    private void OnAttack(InputAction.CallbackContext context)
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + attackCooldown;

        if (animator != null) animator.SetTrigger("Action");

        if (mudBallPrefab != null)
        {
            // Without a throw point, spawn in front of Clodi at chest height (not at his feet)
            Vector3 spawnPosition = throwPoint != null
                ? throwPoint.position
                : transform.position + Vector3.up * 1f + transform.forward * 0.8f;
            GameObject ball = Instantiate(mudBallPrefab, spawnPosition, Quaternion.identity);

            // The ball must not bump into Clodi himself
            if (bodyCollider != null && ball.TryGetComponent(out Collider ballCollider))
            {
                Physics.IgnoreCollision(ballCollider, bodyCollider);
            }

            if (ball.TryGetComponent(out Rigidbody ballBody))
            {
                ballBody.AddForce((transform.forward + Vector3.up * 0.2f) * throwForce, ForceMode.Impulse);
            }
        }
    }

    private void SetWalking(bool isWalking)
    {
        if (animator != null) animator.SetBool("isWalking", isWalking);
    }
}
