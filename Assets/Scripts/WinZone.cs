using UnityEngine;

/// <summary>
/// Clodi's nest: if the player enters with all eggs, the game is won.
/// Collider must have "Is Trigger" checked.
/// </summary>
public class WinZone : MonoBehaviour
{
    [SerializeField] private GameObject notEnoughEggsHint;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || GameManager.Instance == null) return;

        if (GameManager.Instance.HasAllEggs)
        {
            GameManager.Instance.WinGame();
        }
        else if (notEnoughEggsHint != null)
        {
            notEnoughEggsHint.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && notEnoughEggsHint != null)
        {
            notEnoughEggsHint.SetActive(false);
        }
    }
}
