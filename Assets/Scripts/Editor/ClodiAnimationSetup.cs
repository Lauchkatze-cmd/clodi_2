#if UNITY_EDITOR
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fixes Clodi's animations.
///
/// The FBX contains more than 600 animation takes (every object of the Blender scene),
/// but the Animator used the wrong ones: "Swim" for walking and the mud ball's own
/// animation for the attack. The real character animations are in the FBX:
///   Rig_Clodi|Clodi_Idle_V2, Rig_Clodi|Clodi_Walk, Rig_Clodi|Clodi_Attack
///
/// Menu: Tools > Clodi > Setup Clodi Animations
/// What it does:
/// 1. FBX import: keeps only these 3 clips (Idle + Walk looping), no root motion baked into position
/// 2. Builds a new Animator Controller (Idle <-> Walk, Any State -> Attack)
/// 3. Connects it to Player > ClodiModel and tells the scripts to use the real walk animation
/// </summary>
public static class ClodiAnimationSetup
{
    private const string ModelPath = "Assets/Models/Clodi_Project_Talotta.fbx";
    private const string ControllerPath = "Assets/Animations/Clodi_Controller_Fixed.controller";

    private static readonly string[] IdleTakes = { "Rig_Clodi|Clodi_Idle_V2", "Rig_Clodi|Clodi_Idle_OLD", "Idle_1_80" };
    private static readonly string[] WalkTakes = { "Rig_Clodi|Clodi_Walk", "Rig_Clodi|Clodi_Walk_Test_Safe_80_160", "Walk_80_160" };
    private static readonly string[] AttackTakes = { "Rig_Clodi|Clodi_Attack", "Rig_Clodi|Clodi_Attack_Safe_1_80" };

    [MenuItem("Tools/Clodi/Setup Clodi Animations")]
    public static void Setup()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Setup Clodi Animations", "Model not found at " + ModelPath, "OK");
            return;
        }

        StringBuilder report = new StringBuilder();

        // ---------- 1. Import only the clips we need ----------
        ModelImporterClipAnimation[] all = importer.defaultClipAnimations;
        ModelImporterClipAnimation idle = Pick(all, IdleTakes);
        ModelImporterClipAnimation walk = Pick(all, WalkTakes);
        ModelImporterClipAnimation attack = Pick(all, AttackTakes);

        if (idle == null && walk == null)
        {
            EditorUtility.DisplayDialog("Setup Clodi Animations",
                "Could not find Clodi_Idle or Clodi_Walk in the FBX.\nTakes found: " + all.Length, "OK");
            return;
        }

        var clips = new System.Collections.Generic.List<ModelImporterClipAnimation>();
        if (idle != null) clips.Add(Configure(idle, "Clodi_Idle", true));
        if (walk != null) clips.Add(Configure(walk, "Clodi_Walk", true));
        if (attack != null) clips.Add(Configure(attack, "Clodi_Attack", false));

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();

        report.AppendLine($"FBX had {all.Length} animation takes. Kept {clips.Count}:");
        foreach (ModelImporterClipAnimation clip in clips) report.AppendLine($"  {clip.name}  (from {clip.takeName})");

        // ---------- 2. Build a fresh Animator Controller ----------
        AnimationClip[] imported = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        AnimationClip idleClip = imported.FirstOrDefault(c => c.name == "Clodi_Idle");
        AnimationClip walkClip = imported.FirstOrDefault(c => c.name == "Clodi_Walk");
        AnimationClip attackClip = imported.FirstOrDefault(c => c.name == "Clodi_Attack");

        AnimatorController controller = BuildController(idleClip ?? walkClip, walkClip, attackClip);
        report.AppendLine($"New controller: {ControllerPath}");

        // ---------- 3. Connect to the player in the open scene ----------
        GameObject player = GameObject.FindWithTag("Player");
        Transform model = player != null ? player.transform.Find("ClodiModel") : null;
        if (model != null)
        {
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.gameObject.AddComponent<Animator>();
            Undo.RecordObject(animator, "Assign controller");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            ModelPoseLock poseLock = model.GetComponent<ModelPoseLock>();
            if (poseLock != null) SetBool(poseLock, "useWaddle", walkClip == null);

            PlayerController playerController = player.GetComponent<PlayerController>();
            if (playerController != null)
            {
                SetObject(playerController, "animator", animator);
                SetBool(playerController, "useSwimAnimationForWalking", walkClip != null);
            }

            EditorSceneManager.MarkSceneDirty(player.scene);
            report.AppendLine("Connected to Player > ClodiModel.");
            report.AppendLine(walkClip != null
                ? "Real walk animation is used, code waddle turned off."
                : "No walk clip found, code waddle stays on.");
        }
        else
        {
            report.AppendLine("Open the ClodisSwamp scene and run this again to connect the controller.");
        }

        report.AppendLine();
        report.AppendLine("Save the scene (Ctrl+S) and press Play.");
        Debug.Log("[Setup Clodi Animations]\n" + report);
        EditorUtility.DisplayDialog("Setup Clodi Animations", report.ToString(), "OK");
    }

    private static ModelImporterClipAnimation Pick(ModelImporterClipAnimation[] all, string[] preferredTakes)
    {
        foreach (string take in preferredTakes)
        {
            ModelImporterClipAnimation found = all.FirstOrDefault(c => c.takeName == take);
            if (found != null) return found;
        }
        return null;
    }

    private static ModelImporterClipAnimation Configure(ModelImporterClipAnimation clip, string newName, bool loop)
    {
        clip.name = newName;
        clip.loopTime = loop;
        clip.loopPose = loop;
        // Keep Clodi where the script puts him (no drifting or flipping from the animation)
        clip.lockRootRotation = true;
        clip.lockRootHeightY = true;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalOrientation = true;
        clip.keepOriginalPositionY = true;
        clip.keepOriginalPositionXZ = true;
        return clip;
    }

    private static AnimatorController BuildController(AnimationClip idle, AnimationClip walk, AnimationClip attack)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
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
            toWalk.duration = 0.15f;
            toWalk.AddCondition(AnimatorConditionMode.If, 0, "isWalking");

            AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.15f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "isWalking");
        }

        if (attack != null)
        {
            AnimatorState attackState = machine.AddState("Attack");
            attackState.motion = attack;

            AnimatorStateTransition toAttack = machine.AddAnyStateTransition(attackState);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.05f;
            toAttack.canTransitionToSelf = false;
            toAttack.AddCondition(AnimatorConditionMode.If, 0, "Action");

            AnimatorStateTransition back = attackState.AddTransition(idleState);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.15f;
        }

        AssetDatabase.SaveAssets();
        return controller;
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
