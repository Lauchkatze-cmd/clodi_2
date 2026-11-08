using UnityEngine;

/// <summary>
/// Keeps the model standing the way you placed it in the editor.
/// The Blender animations also move and rotate the whole model (the root object),
/// which makes Clodi lie on his back while playing. This script undoes that
/// every frame, but keeps the leg/body animations of the child objects.
///
/// Put it on "ClodiModel" (the object with the Animator).
/// Rotate/move ClodiModel in Edit Mode until he stands correctly – that pose is used in Play Mode.
/// </summary>
[DefaultExecutionOrder(1000)]
public class ModelPoseLock : MonoBehaviour
{
    [Tooltip("Extra rotation, e.g. 0/180/0 if Clodi walks backwards.")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    [Tooltip("Move the model up or down if the feet are in or above the ground.")]
    [SerializeField] private float heightOffset = 0f;

    private Vector3 lockedPosition;
    private Quaternion lockedRotation;
    private Vector3 lockedScale;

    private void Awake()
    {
        lockedPosition = transform.localPosition;
        lockedRotation = transform.localRotation;
        lockedScale = transform.localScale;
    }

    private void LateUpdate()
    {
        // Runs after the Animator, so it overrides the animated root transform
        transform.localPosition = lockedPosition + Vector3.up * heightOffset;
        transform.localRotation = Quaternion.Euler(rotationOffset) * lockedRotation;
        transform.localScale = lockedScale;
    }
}
