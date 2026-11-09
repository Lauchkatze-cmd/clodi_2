#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Replaces the visual model of the Player with a new FBX.
///
/// How to use:
/// 1. Select the new model (FBX) in the Project window.
/// 2. Menu: Tools > Clodi > Replace Player Model With Selected FBX
///
/// What it does:
/// - turns the old "ClodiModel" off (renamed to "ClodiModel_OLD", so you can go back)
/// - puts the new model under Player as "ClodiModel"
/// - removes lights, cameras and colliders that came from Blender
/// - scales it to the player size and puts the feet on the ground
/// - creates a new Animator Controller from the clips inside the FBX
///   (Idle, Walk, Attack – found by their names)
/// - adds ModelPoseLock and connects everything to the PlayerController
/// </summary>
public static class PlayerModelReplacer
{
    private const float TargetHeight = 1.0f;
    private const string ControllerFolder = "Assets/Animations";

    [MenuItem("Tools/Clodi/Replace Player Model With Selected FBX")]
    public static void ReplaceModel()
    {
        GameObject modelAsset = Selection.activeObject as GameObject;
        string assetPath = modelAsset != null ? AssetDatabase.GetAssetPath(modelAsset) : "";
        if (modelAsset == null || string.IsNullOrEmpty(assetPath) || PrefabUtility.GetPrefabAssetType(modelAsset) != PrefabAssetType.Model)
        {
            EditorUtility.DisplayDialog("Replace Player Model",
                "Please select the new model (FBX file) in the Project window first.", "OK");
            return;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Replace Player Model", "No GameObject with tag 'Player' found. Open the game scene first.", "OK");
            return;
        }

        StringBuilder report = new StringBuilder();

        // 1. Turn off the old model (keep it as backup)
        Transform oldModel = player.transform.Find("ClodiModel");
        if (oldModel != null)
        {
            Undo.RecordObject(oldModel.gameObject, "Disable old model");
            oldModel.gameObject.name = "ClodiModel_OLD";
            oldModel.gameObject.SetActive(false);
            report.AppendLine("Old model turned off and renamed to 'ClodiModel_OLD'.");
        }

        // 2. Add the new model
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        Undo.RegisterCreatedObjectUndo(model, "Add new player model");
        model.name = "ClodiModel";
        model.transform.SetParent(player.transform, false);
        model.transform.localPosition = Vector3.zero;

        // 3. Remove things from the Blender scene that don't belong to the character
        int removed = 0;
        foreach (Light light in model.GetComponentsInChildren<Light>(true)) { light.gameObject.SetActive(false); removed++; }
        foreach (Camera camera in model.GetComponentsInChildren<Camera>(true)) { camera.gameObject.SetActive(false); removed++; }
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(collider); }
        if (removed > 0) report.AppendLine($"Turned off {removed} light(s)/camera(s) from Blender.");

        // 4. Scale and place on the ground
        FitModel(model.transform, player.transform, TargetHeight);

        // 5. Animator + controller from the FBX clips
        Animator animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.applyRootMotion = false;

        List<AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__"))
            .ToList();

        AnimationClip idle = FindClip(clips, "idle", "stand", "ruhe");
        AnimationClip walk = FindClip(clips, "walk", "lauf", "geh", "run");
        AnimationClip attack = FindClip(clips, "attack", "wurf", "throw", "matsch", "angriff");

        report.AppendLine($"Clips in FBX: {(clips.Count == 0 ? "none" : string.Join(", ", clips.Select(c => c.name)))}");
        report.AppendLine($"  Idle:   {(idle != null ? idle.name : "not found")}");
        report.AppendLine($"  Walk:   {(walk != null ? walk.name : "not found -> procedural waddle is used")}");
        report.AppendLine($"  Attack: {(attack != null ? attack.name : "not found")}");

        if (clips.Count > 0)
        {
            animator.runtimeAnimatorController = CreateController(modelAsset.name, idle ?? clips[0], walk, attack);
            report.AppendLine($"New Animator Controller: {ControllerFolder}/{modelAsset.name}_Controller.controller");
        }

        // 6. Keep the pose stable and connect to the PlayerController
        ModelPoseLock poseLock = model.GetComponent<ModelPoseLock>();
        if (poseLock == null) poseLock = Undo.AddComponent<ModelPoseLock>(model);
        SetBool(poseLock, "useWaddle", walk == null);

        PlayerController controller = player.GetComponent<PlayerController>();
        if (controller != null)
        {
            SetObject(controller, "animator", animator);
            SetBool(controller, "useSwimAnimationForWalking", walk != null);
        }

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = model;

        report.AppendLine();
        report.AppendLine("Next: check in the Scene view that the model stands on its feet and looks along the BLUE arrow.");
        report.AppendLine("If not, rotate 'ClodiModel' (not 'Player'). Then: Tools > Clodi > Fit Collider To Model, and save (Ctrl+S).");

        Debug.Log("[Replace Player Model]\n" + report);
        EditorUtility.DisplayDialog("Replace Player Model", report.ToString(), "OK");
    }

    private static AnimationClip FindClip(List<AnimationClip> clips, params string[] keywords)
    {
        return clips.FirstOrDefault(clip => keywords.Any(k => clip.name.ToLowerInvariant().Contains(k)));
    }

    private static AnimatorController CreateController(string modelName, AnimationClip idle, AnimationClip walk, AnimationClip attack)
    {
        if (!AssetDatabase.IsValidFolder(ControllerFolder)) AssetDatabase.CreateFolder("Assets", "Animations");
        string path = $"{ControllerFolder}/{modelName}_Controller.controller";
        AssetDatabase.DeleteAsset(path);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("isWalking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Action", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idleState = machine.AddState("Idle");
        idleState.motion = idle;
        machine.defaultState = idleState;

        if (walk != null)
        {
            AnimatorState walkState = machine.AddState("Walk");
            walkState.motion = walk;

            AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.1f;
            toWalk.AddCondition(AnimatorConditionMode.If, 0, "isWalking");

            AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.1f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "isWalking");
        }

        if (attack != null)
        {
            AnimatorState attackState = machine.AddState("Attack");
            attackState.motion = attack;

            AnimatorStateTransition toAttack = machine.AddAnyStateTransition(attackState);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.05f;
            toAttack.AddCondition(AnimatorConditionMode.If, 0, "Action");

            AnimatorStateTransition back = attackState.AddTransition(idleState);
            back.hasExitTime = true;
            back.exitTime = 0.95f;
            back.duration = 0.1f;
        }

        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void FitModel(Transform model, Transform root, float targetHeight)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return;

        Bounds bounds = GetBounds(renderers);
        if (bounds.size.y > 0.001f) model.localScale *= targetHeight / bounds.size.y;

        bounds = GetBounds(renderers);
        model.position -= new Vector3(bounds.center.x - root.position.x, bounds.min.y - root.position.y, bounds.center.z - root.position.z);
    }

    private static Bounds GetBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void SetBool(Object target, string field, bool value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null) return;
        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObject(Object target, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null) return;
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
