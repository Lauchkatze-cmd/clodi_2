using UnityEngine;

public class PoisonAffect : MonoBehaviour
{
    private float storedWalkSpeed;
    private bool playerInside;

    private void OnTriggerEnter(Collider other)
    {
        if (playerInside) return;
        if (!other.TryGetComponent(out PlayerController player)) return;

        storedWalkSpeed = player.WalkSpeed;
        player.WalkSpeed = 1f;
        playerInside = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!playerInside) return;
        if (!other.TryGetComponent(out PlayerController player)) return;

        player.WalkSpeed = storedWalkSpeed;
        playerInside = false;
    }
}
