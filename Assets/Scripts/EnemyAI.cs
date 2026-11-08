using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Swamp Guard AI. Uses a NavMeshAgent for movement and a Behavior Tree for decisions.
///
/// Behavior Tree:
/// Root (Selector)
///  ├─ Sequence: Is stunned?          → Stunned
///  ├─ Sequence: Sees player? + Close → Attack
///  ├─ Sequence: Sees player?         → Chase
///  ├─ Sequence: Remembers player?    → Search (go to last known position, look around)
///  └─ Patrol (walk between waypoints)
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyStatus))]
public class EnemyAI : MonoBehaviour
{
    [Header("Patrol")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float waitAtPointTime = 1f;

    [Header("Senses")]
    [SerializeField] private float viewDistance = 14f;
    [SerializeField] private float viewAngle = 120f;
    [SerializeField] private float hearingDistance = 3f;
    [SerializeField] private float eyeHeight = 1.5f;

    [Header("Chase and Attack")]
    [SerializeField] private float chaseSpeed = 3.8f;
    [SerializeField] private float attackRange = 1.9f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private int attackDamage = 1;

    [Header("Search")]
    [SerializeField] private float searchDuration = 4f;

    [Header("Visuals")]
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Color normalColor = new Color(0.25f, 0.4f, 0.2f);
    [SerializeField] private Color alertColor = new Color(0.85f, 0.25f, 0.15f);
    [SerializeField] private Color stunnedColor = new Color(0.4f, 0.6f, 1f);
    [SerializeField] private bool showStateLabel = true;

    private NavMeshAgent agent;
    private EnemyStatus status;
    private Transform player;
    private PlayerHealth playerHealth;
    private BTNode behaviorTree;
    private Material bodyMaterial;
    private TextMesh stateLabel;

    private bool canSeePlayer;
    private bool hasLastKnownPosition;
    private Vector3 lastKnownPosition;
    private float searchTimer;
    private float waitTimer;
    private int patrolIndex;
    private float nextAttackTime;
    private string currentState = "Idle";

    public string CurrentState => currentState;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        status = GetComponent<EnemyStatus>();
        if (bodyRenderer != null) bodyMaterial = bodyRenderer.material;
        BuildBehaviorTree();
    }

    private void Start()
    {
        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }

        // Make sure the agent stands on the NavMesh
        if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }

        if (showStateLabel) CreateStateLabel();
    }

    private void BuildBehaviorTree()
    {
        behaviorTree = new BTSelector("Root",
            new BTSequence("Stunned Branch",
                new BTCondition("Is Stunned?", () => status.IsStunned),
                new BTAction("Stunned", Stunned)),
            new BTSequence("Attack Branch",
                new BTCondition("Sees Player?", () => canSeePlayer),
                new BTCondition("Player In Range?", IsPlayerInAttackRange),
                new BTAction("Attack", Attack)),
            new BTSequence("Chase Branch",
                new BTCondition("Sees Player?", () => canSeePlayer),
                new BTAction("Chase", Chase)),
            new BTSequence("Search Branch",
                new BTCondition("Remembers Player?", () => hasLastKnownPosition),
                new BTAction("Search", Search)),
            new BTAction("Patrol", Patrol));
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
        if (player == null || !agent.isOnNavMesh) return;

        UpdateSenses();
        behaviorTree.Tick();
        UpdateColor();
    }

    private void LateUpdate()
    {
        // Label always faces the camera
        if (stateLabel != null && Camera.main != null)
        {
            stateLabel.text = currentState;
            stateLabel.transform.rotation = Camera.main.transform.rotation;
        }
    }

    // ---------- Senses ----------

    private void UpdateSenses()
    {
        canSeePlayer = CheckCanSeePlayer();
        if (canSeePlayer)
        {
            lastKnownPosition = player.position;
            hasLastKnownPosition = true;
            searchTimer = 0f;
        }
    }

    private bool CheckCanSeePlayer()
    {
        Vector3 eyePosition = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPosition = player.position + Vector3.up * 0.75f;
        Vector3 toPlayer = targetPosition - eyePosition;
        float distance = toPlayer.magnitude;

        if (distance > viewDistance) return false;

        // Outside of hearing distance the player must be inside the view cone
        if (distance > hearingDistance)
        {
            Vector3 flatDirection = new Vector3(toPlayer.x, 0f, toPlayer.z);
            if (Vector3.Angle(transform.forward, flatDirection) > viewAngle * 0.5f) return false;
        }

        // Nothing may block the line of sight
        if (Physics.Raycast(eyePosition, toPlayer.normalized, out RaycastHit hit, distance + 0.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == player || hit.transform.IsChildOf(player);
        }
        return false;
    }

    private bool IsPlayerInAttackRange()
    {
        return FlatDistance(transform.position, player.position) <= attackRange;
    }

    // ---------- Actions ----------

    private BTState Stunned()
    {
        currentState = "Stunned";
        agent.isStopped = true;
        agent.ResetPath();
        transform.Rotate(0f, 180f * Time.deltaTime, 0f); // dizzy spinning
        return BTState.Running;
    }

    private BTState Attack()
    {
        currentState = "Attack";
        agent.isStopped = true;
        FaceTowards(player.position);

        if (Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldown;
            if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
        }
        return BTState.Running;
    }

    private BTState Chase()
    {
        currentState = "Chase";
        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.SetDestination(player.position);
        return BTState.Running;
    }

    private BTState Search()
    {
        currentState = "Search";
        agent.isStopped = false;
        agent.speed = patrolSpeed;
        MoveTo(lastKnownPosition);

        if (HasReachedDestination())
        {
            searchTimer += Time.deltaTime;
            transform.Rotate(0f, 90f * Time.deltaTime, 0f); // look around

            if (searchTimer >= searchDuration)
            {
                hasLastKnownPosition = false;
                searchTimer = 0f;
                return BTState.Success;
            }
        }
        return BTState.Running;
    }

    private BTState Patrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            currentState = "Idle";
            agent.isStopped = true;
            return BTState.Running;
        }

        currentState = "Patrol";
        agent.isStopped = false;
        agent.speed = patrolSpeed;

        Transform point = patrolPoints[patrolIndex];
        if (point == null)
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            return BTState.Running;
        }

        MoveTo(point.position);

        if (HasReachedDestination())
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= waitAtPointTime)
            {
                waitTimer = 0f;
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            }
        }
        return BTState.Running;
    }

    // ---------- Helpers ----------

    private void MoveTo(Vector3 target)
    {
        // Only set a new path if the target really changed (keeps remainingDistance reliable)
        if (!agent.hasPath || FlatDistance(agent.destination, target) > 0.5f)
        {
            agent.SetDestination(target);
        }
    }

    private bool HasReachedDestination()
    {
        return !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f;
    }

    private void FaceTowards(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void UpdateColor()
    {
        if (bodyMaterial == null) return;

        Color target = normalColor;
        if (currentState == "Stunned") target = stunnedColor;
        else if (currentState == "Attack" || currentState == "Chase") target = alertColor;
        else if (currentState == "Search") target = Color.Lerp(normalColor, alertColor, 0.5f);

        bodyMaterial.color = Color.Lerp(bodyMaterial.color, target, 10f * Time.deltaTime);
    }

    private void CreateStateLabel()
    {
        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (font == null) return;

        GameObject labelObject = new GameObject("StateLabel");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 2.6f, 0f);

        stateLabel = labelObject.AddComponent<TextMesh>();
        stateLabel.font = font;
        stateLabel.fontSize = 48;
        stateLabel.characterSize = 0.08f;
        stateLabel.anchor = TextAnchor.MiddleCenter;
        stateLabel.alignment = TextAlignment.Center;
        stateLabel.color = Color.white;

        MeshRenderer labelRenderer = labelObject.GetComponent<MeshRenderer>();
        if (labelRenderer == null) labelRenderer = labelObject.AddComponent<MeshRenderer>();
        labelRenderer.sharedMaterial = font.material;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
