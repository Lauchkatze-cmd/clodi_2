using UnityEngine;

/// <summary>
/// Camera follows the player from behind and above, without rotating with him.
/// Put it on the Main Camera. Do NOT make the camera a child of the player.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 6f, -8f);
    [SerializeField] private float smoothTime = 0.2f;

    private Vector3 velocity;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);
        transform.LookAt(target.position + Vector3.up * 1f);
    }
}
