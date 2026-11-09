using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps Clodi standing the way you placed him in the editor and adds a cute waddle while walking.
///
/// Problem it solves: the Blender animations also move and rotate the whole model,
/// which flips Clodi over in Play Mode. This script undoes that every frame.
/// The FBX has no walk animation (only Idle, Swim, Attack), so the walk is made here in code:
/// the body bobs and tilts from side to side and the four feet swing like a real waddle.
///
/// Put it on "ClodiModel" (the object with the Animator).
/// </summary>
[DefaultExecutionOrder(1000)]
public class ModelPoseLock : MonoBehaviour
{
    [Header("Pose")]
    [Tooltip("Extra rotation, e.g. 0/180/0 if Clodi walks backwards, 0/90/0 if he walks sideways.")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    [Tooltip("Move the model up or down if the feet are in or above the ground.")]
    [SerializeField] private float heightOffset = 0f;

    [Header("Procedural Waddle (walk animation)")]
    [SerializeField] private bool useWaddle = true;
    [SerializeField] private float stepsPerSecond = 4f;
    [SerializeField] private float bobHeight = 0.04f;
    [SerializeField] private float sideTilt = 6f;
    [SerializeField] private float footSwingAngle = 25f;
    [Tooltip("Part of the object name that marks a foot (Blender objects are called Clodi_Fuss_HL, ...).")]
    [SerializeField] private string footNameKeyword = "Fuss";

    private Vector3 lockedPosition;
    private Quaternion lockedRotation;
    private Vector3 lockedScale;

    private Rigidbody playerBody;
    private readonly List<Transform> feet = new List<Transform>();
    private readonly List<Quaternion> footBaseRotations = new List<Quaternion>();
    private readonly List<float> footPhases = new List<float>();
    private float walkCycle;
    private float walkBlend;

    private void Awake()
    {
        lockedPosition = transform.localPosition;
        lockedRotation = transform.localRotation;
        lockedScale = transform.localScale;
        playerBody = GetComponentInParent<Rigidbody>();

        // Find the four feet. Diagonal pairs move together (front-left + back-right), like real animals.
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform || !child.name.Contains(footNameKeyword)) continue;
            feet.Add(child);
            footBaseRotations.Add(child.localRotation);
            bool isFrontLeftOrBackRight = child.name.EndsWith("VL") || child.name.EndsWith("HR");
            footPhases.Add(isFrontLeftOrBackRight ? 0f : Mathf.PI);
        }
    }

    private void LateUpdate()
    {
        // Runs after the Animator, so it overrides the animated root transform
        float speed = 0f;
        if (playerBody != null)
        {
            Vector3 velocity = playerBody.linearVelocity;
            velocity.y = 0f;
            speed = velocity.magnitude;
        }

        float target = useWaddle && speed > 0.2f ? 1f : 0f;
        walkBlend = Mathf.MoveTowards(walkBlend, target, Time.deltaTime * 5f);
        walkCycle += Time.deltaTime * stepsPerSecond * Mathf.PI * Mathf.Clamp(speed / 5f, 0.5f, 1.5f);

        float bob = Mathf.Abs(Mathf.Sin(walkCycle)) * bobHeight * walkBlend;
        float tilt = Mathf.Sin(walkCycle) * sideTilt * walkBlend;

        transform.localPosition = lockedPosition + Vector3.up * (heightOffset + bob);
        transform.localRotation = Quaternion.Euler(rotationOffset) * Quaternion.Euler(0f, 0f, tilt) * lockedRotation;
        transform.localScale = lockedScale;

        // Feet swing forward and back
        for (int i = 0; i < feet.Count; i++)
        {
            if (feet[i] == null) continue;
            float swing = Mathf.Sin(walkCycle + footPhases[i]) * footSwingAngle * walkBlend;
            feet[i].localRotation = footBaseRotations[i] * Quaternion.Euler(swing, 0f, 0f);
        }
    }
}
