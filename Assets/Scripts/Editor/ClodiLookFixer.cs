#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Two helper tools for Clodi:
/// Tools > Clodi > Apply Clodsire Colors   – brown swamp colors instead of the white/grey FBX look
/// Tools > Clodi > Fit Collider To Model    – makes the physics capsule match Clodi's real body
/// Tools > Clodi > List Model Parts         – prints all parts and materials to the Console
/// </summary>
public static class ClodiLookFixer
{
    private const string MaterialFolder = "Assets/Materials/Clodi";

    // Color palette inspired by a brown swamp creature
    private static readonly Color BodyBrown = new Color(0.42f, 0.29f, 0.22f);
    private static readonly Color BellyBeige = new Color(0.86f, 0.76f, 0.62f);
    private static readonly Color SpotColor = new Color(0.30f, 0.20f, 0.15f);
    private static readonly Color FeetColor = new Color(0.33f, 0.22f, 0.17f);
    private static readonly Color EyeBlack = new Color(0.05f, 0.05f, 0.05f);
    private static readonly Color EyeWhite = new Color(0.95f, 0.95f, 0.95f);
    private static readonly Color MouthColor = new Color(0.55f, 0.25f, 0.25f);

    [MenuItem("Tools/Clodi/Apply Clodsire Colors")]
    public static void ApplyColors()
    {
        Transform model = FindModel();
        if (model == null) return;

        EnsureFolder();
        Dictionary<string, Material> cache = new Dictionary<string, Material>();
        StringBuilder report = new StringBuilder("Applied colors:\n");

        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] current = renderer.sharedMaterials;
            Material[] replaced = new Material[current.Length];

            for (int i = 0; i < current.Length; i++)
            {
                string materialName = current[i] != null ? current[i].name : "";
                string key = ChooseColorKey(renderer.name, materialName);
                replaced[i] = GetMaterial(key, cache);
                report.AppendLine($"  {renderer.name} [{materialName}] -> {key}");
            }

            Undo.RecordObject(renderer, "Apply Clodi colors");
            renderer.sharedMaterials = replaced;
        }

        EditorSceneManager.MarkSceneDirty(model.gameObject.scene);
        Debug.Log("[Clodi Colors]\n" + report);
        EditorUtility.DisplayDialog("Clodi Colors",
            "Colors applied. The full list is in the Console.\n\n" +
            "You can change any color later: Assets/Materials/Clodi.\n" +
            "Save the scene with Ctrl+S.", "OK");
    }

    /// <summary>Decides the color by object name or material name (German or English).</summary>
    private static string ChooseColorKey(string objectName, string materialName)
    {
        string text = (objectName + " " + materialName).ToLowerInvariant();

        if (text.Contains("pupil") || text.Contains("pupille")) return "EyeBlack";
        if (text.Contains("auge") || text.Contains("eye")) return text.Contains("weiss") || text.Contains("white") ? "EyeWhite" : "EyeBlack";
        if (text.Contains("mund") || text.Contains("mouth") || text.Contains("zunge")) return "Mouth";
        if (text.Contains("bauch") || text.Contains("belly")) return "Belly";
        if (text.Contains("fleck") || text.Contains("spot")) return "Spot";
        if (text.Contains("fuss") || text.Contains("foot") || text.Contains("feet")) return "Feet";
        return "Body";
    }

    private static Material GetMaterial(string key, Dictionary<string, Material> cache)
    {
        if (cache.TryGetValue(key, out Material cached)) return cached;

        string path = $"{MaterialFolder}/Clodi_{key}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader);
            Color color = key switch
            {
                "Belly" => BellyBeige,
                "Spot" => SpotColor,
                "Feet" => FeetColor,
                "EyeBlack" => EyeBlack,
                "EyeWhite" => EyeWhite,
                "Mouth" => MouthColor,
                _ => BodyBrown
            };
            material.SetColor("_BaseColor", color);
            material.color = color;
            material.SetFloat("_Smoothness", key.StartsWith("Eye") ? 0.8f : 0.45f); // a bit slimy
            AssetDatabase.CreateAsset(material, path);
        }

        cache[key] = material;
        return material;
    }

    [MenuItem("Tools/Clodi/Fit Collider To Model")]
    public static void FitCollider()
    {
        Transform model = FindModel();
        if (model == null) return;

        Transform player = model.parent;
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            EditorUtility.DisplayDialog("Fit Collider", "Player has no Capsule Collider.", "OK");
            return;
        }

        List<Renderer> renderers = new List<Renderer>();
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer.enabled) renderers.Add(renderer);
        }
        if (renderers.Count == 0) return;

        Bounds world = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++) world.Encapsulate(renderers[i].bounds);

        // Convert to the player's local space (player has no rotation in edit mode)
        Vector3 localCenter = player.InverseTransformPoint(world.center);
        Vector3 size = world.size;

        Undo.RecordObject(capsule, "Fit collider");
        // Clodi is long and flat: use a capsule lying along the longest horizontal axis
        if (size.z >= size.x)
        {
            capsule.direction = 2; // Z
            capsule.height = size.z;
        }
        else
        {
            capsule.direction = 0; // X
            capsule.height = size.x;
        }
        capsule.radius = Mathf.Min(size.y, Mathf.Min(size.x, size.z)) * 0.5f;
        capsule.center = new Vector3(localCenter.x, capsule.radius, localCenter.z);

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorUtility.DisplayDialog("Fit Collider",
            $"Capsule fitted: length {capsule.height:0.00} m, radius {capsule.radius:0.00} m.\n" +
            "Check the green capsule in the Scene view and save with Ctrl+S.", "OK");
    }

    [MenuItem("Tools/Clodi/List Model Parts")]
    public static void ListParts()
    {
        Transform model = FindModel();
        if (model == null) return;

        StringBuilder report = new StringBuilder("Clodi model parts:\n");
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            report.Append($"  {renderer.name} ({renderer.GetType().Name}, active: {renderer.gameObject.activeInHierarchy}) materials: ");
            foreach (Material material in renderer.sharedMaterials) report.Append(material != null ? material.name + "  " : "none  ");
            report.AppendLine();
        }
        Debug.Log(report.ToString());
        EditorUtility.DisplayDialog("Clodi Parts", "The list is in the Console window.", "OK");
    }

    private static Transform FindModel()
    {
        GameObject player = GameObject.FindWithTag("Player");
        Transform model = player != null ? player.transform.Find("ClodiModel") : null;
        if (model == null)
        {
            EditorUtility.DisplayDialog("Clodi", "Could not find Player > ClodiModel in the open scene.", "OK");
        }
        return model;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Clodi");
    }
}
#endif
