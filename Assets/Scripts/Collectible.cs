using UnityEngine;

/// <summary>
/// A swamp egg the player can collect. Collider must have "Is Trigger" checked.
/// Make this a Prefab (base collectible) and create Prefab Variants from it.
/// </summary>
public class Collectible : MonoBehaviour
{
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private float bobHeight = 0.2f;
    [SerializeField] private float bobSpeed = 2f;

    private Vector3 startPosition;

    private void Start()
    {
        startPosition = transform.position;
    }

    private void Update()
    {
        // Small spin and float animation so it is easy to see
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        transform.position = startPosition + Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobHeight;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddEgg();

            if (GameFeedback.Instance != null)
            {
                GameFeedback.Instance.EggCollected(transform.position,
                    GameManager.Instance.CollectedEggs, GameManager.Instance.EggsToCollect);
            }
        }

        Destroy(gameObject);
    }
}
