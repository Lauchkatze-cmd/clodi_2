using UnityEngine;

/// <summary>
/// Swamp pit: if the player falls in, the game is lost.
/// Use a big Box Collider with "Is Trigger" checked under the pit.
/// </summary>
public class DeathZone : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (GameManager.Instance != null) GameManager.Instance.LoseGame();
    }
}
