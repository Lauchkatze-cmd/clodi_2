using UnityEngine;

/// <summary>
/// The mud ball Clodi throws. Stuns enemies it hits.
/// Prefab needs: Sphere Collider + Rigidbody (Use Gravity on).
/// </summary>
public class MudBall : MonoBehaviour
{
    [SerializeField] private float stunDuration = 3f;
    [SerializeField] private float lifeTime = 4f;

    private void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player")) return;

        EnemyStatus enemy = collision.gameObject.GetComponentInParent<EnemyStatus>();
        if (enemy != null)
        {
            enemy.Stun(stunDuration);

            if (GameFeedback.Instance != null)
            {
                Vector3 hitPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
                GameFeedback.Instance.EnemyHit(hitPoint);
            }
        }

        Destroy(gameObject);
    }
}
