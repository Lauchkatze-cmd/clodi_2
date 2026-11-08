#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Cleans up the imported Clodi model inside the Player:
/// - turns off lights and cameras that came from the Blender scene (they make the screen white)
/// - turns off environment objects from the Blender scene (water, lily pads, reeds, props)
/// - keeps only the rigged character mesh and rescales it to the player size
/// Menu: Tools > Fix Clodi Model
/// </summary>
public static class ClodiModelFixer
{
    private const float TargetHeight = 1.5f;

    [MenuItem("Tools/Fix Clodi Model")]
    public static void FixModel()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Fix Clodi Model", "No GameObject with tag 'Player' found in the open scene.", "OK");
            return;
        }

        Transform model = player.transform.Find("ClodiModel");
        if (model == null)
        {
            EditorUtility.DisplayDialog("Fix Clodi Model", "No child called 'ClodiModel' found under Player.", "OK");
            return;
        }

        StringBuilder report = new StringBuilder();
        List<string> disabled = new List<string>();

        // 1. Lights and cameras from Blender
        foreach (Light light in model.GetComponentsInChildren<Light>(true))
        {
            Undo.RecordObject(light.gameObject, "Disable Blender light");
            light.gameObject.SetActive(false);
            disabled.Add("Light: " + light.name);
        }
        foreach (Camera camera in model.GetComponentsInChildren<Camera>(true))
        {
            Undo.RecordObject(camera.gameObject, "Disable Blender camera");
            camera.gameObject.SetActive(false);
            disabled.Add("Camera: " + camera.name);
        }

        // 2. Environment objects: non-rigged meshes that are direct children of the model
        foreach (Transform child in model)
        {
            if (!child.gameObject.activeSelf) continue;
            bool hasSkinnedMesh = child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
            bool hasPlainMesh = child.GetComponent<MeshRenderer>() != null;
            if (hasPlainMesh && !hasSkinnedMesh)
            {
                Undo.RecordObject(child.gameObject, "Disable environment object");
                child.gameObject.SetActive(false);
                disabled.Add("Object: " + child.name);
            }
        }

        // 3. Rescale using only the rigged character mesh
        SkinnedMeshRenderer[] skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        if (skinned.Length > 0)
        {
            Undo.RecordObject(model, "Rescale Clodi");
            Bounds bounds = GetBounds(skinned);
            if (bounds.size.y > 0.001f) model.localScale *= TargetHeight / bounds.size.y;

            bounds = GetBounds(skinned);
            Vector3 offset = new Vector3(
                bounds.center.x - player.transform.position.x,
                bounds.min.y - player.transform.position.y,
                bounds.center.z - player.transform.position.z);
            model.position -= offset;
        }
        else
        {
            report.AppendLine("WARNING: No rigged mesh found – scale was not changed.");
        }

        EditorSceneManager.MarkSceneDirty(player.scene);

        report.AppendLine($"Turned off {disabled.Count} object(s):");
        foreach (string name in disabled) report.AppendLine("  - " + name);
        report.AppendLine();
        report.AppendLine("Still visible (character meshes):");
        foreach (SkinnedMeshRenderer renderer in skinned) report.AppendLine("  - " + renderer.name);

        Debug.Log("[Fix Clodi Model]\n" + report);
        EditorUtility.DisplayDialog("Fix Clodi Model",
            report + "\nIf something important was turned off, you can switch it back on in the Hierarchy. " +
            "Don't forget to save the scene (Ctrl+S).", "OK");
    }

    private static Bounds GetBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
#endif
