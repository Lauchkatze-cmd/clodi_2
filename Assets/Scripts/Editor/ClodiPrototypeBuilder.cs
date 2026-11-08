#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor tool that builds the whole "Clodi's Swamp" prototype scene.
/// Menu: Tools > Build Clodi Prototype
/// Must be inside a folder called "Editor".
/// </summary>
public static class ClodiPrototypeBuilder
{
    private const string GeneratedFolder = "Assets/Generated";
    private const string MaterialFolder = GeneratedFolder + "/Materials";
    private const string PrefabFolder = GeneratedFolder + "/Prefabs";
    private const string TerrainFolder = GeneratedFolder + "/Terrain";
    private const string ScenePath = "Assets/Scenes/ClodisSwamp.unity";

    private const float MapSize = 100f;
    private const float MaxTerrainHeight = 20f;
    private const float BaseHeight = 2f;
    private const float PitMinX = 44f;
    private const float PitMaxX = 56f;
    private const float PitEdgeWidth = 2f;
    private const int EggCount = 5;

    private static Terrain terrain;
    private static Font uiFont;
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    [MenuItem("Tools/Build Clodi Prototype")]
    public static void Build()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Build Clodi Prototype",
            "This creates the scene 'Assets/Scenes/ClodisSwamp.unity' and the folder 'Assets/Generated'.\n\n" +
            "If they already exist, they will be replaced. Continue?",
            "Build it!", "Cancel");
        if (!confirmed) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        try
        {
            Progress("Preparing folders", 0.05f);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            PrepareFolders();
            materials.Clear();
            uiFont = LoadBuiltinFont();

            Progress("Creating materials", 0.1f);
            CreateAllMaterials();

            Progress("Shaping terrain", 0.2f);
            terrain = CreateTerrain();

            Progress("Building level with ProBuilder", 0.35f);
            Transform level = new GameObject("Level (ProBuilder)").transform;
            BuildLevel(level);

            Progress("Baking NavMesh", 0.5f);
            BakeNavMesh();

            Progress("Creating prefabs", 0.6f);
            GameObject mudBallPrefab = CreateMudBallPrefab();
            GameObject eggPrefab = CreateEggPrefabs();
            GameObject enemyPrefab = CreateEnemyPrefab();

            Progress("Creating UI", 0.7f);
            Transform gameplay = new GameObject("Gameplay").transform;
            UiReferences ui = CreateUi();
            CreateGameManager(ui);

            Progress("Placing player, eggs and zones", 0.8f);
            GameObject player = CreatePlayer(Ground(15f, 15f) + Vector3.up * 0.1f, mudBallPrefab);
            PlaceEggs(eggPrefab, gameplay);
            CreateZones(gameplay, ui.nestHint);

            Progress("Placing enemies", 0.9f);
            PlaceEnemies(enemyPrefab);

            Progress("Camera, light and saving", 0.95f);
            SetupCameraAndLight(player.transform);
            SaveSceneAndBuildSettings(scene);

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Done!",
                "Clodi's Swamp is ready.\n\nPress Play to test it.\n\n" +
                "If Clodi looks the wrong way, rotate the child 'ClodiModel' (not 'Player').", "OK");
        }
        catch (System.Exception exception)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Build failed",
                "Something went wrong. Check the Console for the red error message.\n\n" + exception.Message, "OK");
        }
    }

    // =====================================================================
    // Folders and helpers
    // =====================================================================

    private static void Progress(string message, float value)
    {
        EditorUtility.DisplayProgressBar("Building Clodi's Swamp", message, value);
    }

    private static void PrepareFolders()
    {
        if (AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.DeleteAsset(GeneratedFolder);
        EnsureFolder("Assets", "Generated");
        EnsureFolder(GeneratedFolder, "Materials");
        EnsureFolder(GeneratedFolder, "Prefabs");
        EnsureFolder(GeneratedFolder, "Terrain");
        EnsureFolder("Assets", "Scenes");
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    private static Font LoadBuiltinFont()
    {
        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        return font;
    }

    private static T FindAsset<T>(string filter, string preferredName) where T : Object
    {
        T fallback = null;
        foreach (string guid in AssetDatabase.FindAssets(filter))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) continue;
            if (asset.name == preferredName) return asset;
            if (fallback == null) fallback = asset;
        }
        return fallback;
    }

    private static void SetField(Object target, string fieldName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogWarning($"Field '{fieldName}' not found on {target.name}");
            return;
        }
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetInt(Object target, string fieldName, int value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null) return;
        property.intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectArray(Object target, string fieldName, Object[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null) return;
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RemoveCollider(GameObject gameObject)
    {
        Collider collider = gameObject.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
    }

    /// <summary>World position on the terrain surface.</summary>
    private static Vector3 Ground(float x, float z)
    {
        float y = terrain != null
            ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y
            : HeightAt(x, z);
        return new Vector3(x, y, z);
    }

    // =====================================================================
    // Materials
    // =====================================================================

    private static void CreateAllMaterials()
    {
        CreateMaterial("Wood", new Color(0.42f, 0.28f, 0.16f), 0.15f);
        CreateMaterial("DarkWood", new Color(0.28f, 0.19f, 0.11f), 0.1f);
        CreateMaterial("Stone", new Color(0.45f, 0.46f, 0.42f), 0.2f);
        CreateMaterial("MudWater", new Color(0.22f, 0.16f, 0.08f), 0.85f);
        CreateMaterial("MudBall", new Color(0.32f, 0.21f, 0.1f), 0.5f);
        CreateMaterial("EggBase", new Color(0.92f, 0.9f, 0.8f), 0.4f);
        CreateMaterial("EggGlow", new Color(0.65f, 1f, 0.55f), 0.6f, new Color(0.35f, 0.9f, 0.3f) * 1.5f);
        CreateMaterial("Guard", new Color(0.25f, 0.4f, 0.2f), 0.3f);
        CreateMaterial("GuardEyes", new Color(1f, 0.9f, 0.2f), 0.5f, new Color(1f, 0.8f, 0.1f) * 2f);
        CreateMaterial("LilyPad", new Color(0.2f, 0.5f, 0.2f), 0.4f);
        CreateMaterial("Reed", new Color(0.45f, 0.5f, 0.2f), 0.1f);
        CreateMaterial("Nest", new Color(0.55f, 0.4f, 0.2f), 0.05f);
        CreateMaterial("Placeholder", new Color(0.55f, 0.5f, 0.75f), 0.3f);
    }

    private static Material CreateMaterial(string name, Color color, float smoothness, Color? emission = null)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.color = color;
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Glossiness", smoothness);

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, $"{MaterialFolder}/{name}.mat");
        materials[name] = material;
        return material;
    }

    private static Material Mat(string name)
    {
        return materials.TryGetValue(name, out Material material) ? material : null;
    }

    // =====================================================================
    // Terrain (Unity Terrain Tools requirement)
    // =====================================================================

    /// <summary>Height of the landscape in meters at a world position.</summary>
    private static float HeightAt(float x, float z)
    {
        float height = BaseHeight;

        // Hills around the border keep the player inside the map
        float edgeDistance = Mathf.Min(Mathf.Min(x, MapSize - x), Mathf.Min(z, MapSize - z));
        if (edgeDistance < 12f)
        {
            float t = 1f - edgeDistance / 12f;
            height += t * t * 9f;
        }

        // Small natural bumps
        height += (Mathf.PerlinNoise(x * 0.08f, z * 0.08f) - 0.5f) * 0.16f;

        // The swamp pit cuts through the middle of the map
        height = Mathf.Lerp(height, 0f, PitFactor(x));
        return height;
    }

    /// <summary>0 = normal ground, 1 = bottom of the pit.</summary>
    private static float PitFactor(float x)
    {
        if (x <= PitMinX || x >= PitMaxX) return 0f;
        float distanceInside = Mathf.Min(x - PitMinX, PitMaxX - x);
        return Mathf.SmoothStep(0f, 1f, distanceInside / PitEdgeWidth);
    }

    private static Terrain CreateTerrain()
    {
        TerrainData data = new TerrainData();
        AssetDatabase.CreateAsset(data, TerrainFolder + "/SwampTerrainData.asset");

        data.heightmapResolution = 129;
        data.size = new Vector3(MapSize, MaxTerrainHeight, MapSize);

        int resolution = data.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        for (int zIndex = 0; zIndex < resolution; zIndex++)
        {
            for (int xIndex = 0; xIndex < resolution; xIndex++)
            {
                float x = xIndex / (float)(resolution - 1) * MapSize;
                float z = zIndex / (float)(resolution - 1) * MapSize;
                heights[zIndex, xIndex] = HeightAt(x, z) / MaxTerrainHeight;
            }
        }
        data.SetHeights(0, 0, heights);

        // Terrain layers (painted textures)
        TerrainLayer grass = CreateTerrainLayer("Grass", new Color(0.3f, 0.42f, 0.18f), new Color(0.2f, 0.32f, 0.12f), 1);
        TerrainLayer mud = CreateTerrainLayer("Mud", new Color(0.3f, 0.22f, 0.12f), new Color(0.2f, 0.14f, 0.07f), 2);
        TerrainLayer moss = CreateTerrainLayer("Moss", new Color(0.36f, 0.4f, 0.16f), new Color(0.25f, 0.3f, 0.1f), 3);
        data.terrainLayers = new[] { grass, mud, moss };

        data.alphamapResolution = 128;
        int alphaResolution = data.alphamapResolution;
        float[,,] alphas = new float[alphaResolution, alphaResolution, 3];
        for (int zIndex = 0; zIndex < alphaResolution; zIndex++)
        {
            for (int xIndex = 0; xIndex < alphaResolution; xIndex++)
            {
                float x = xIndex / (float)(alphaResolution - 1) * MapSize;
                float z = zIndex / (float)(alphaResolution - 1) * MapSize;

                float distanceToPit = Mathf.Max(0f, Mathf.Max(PitMinX - x, x - PitMaxX));
                float mudWeight = 1f - Mathf.Clamp01(distanceToPit / 4f);
                float mossWeight = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.05f + 10f, z * 0.05f + 10f) - 0.45f) * 3f) * (1f - mudWeight);
                float grassWeight = Mathf.Max(0f, 1f - mudWeight - mossWeight);

                float sum = grassWeight + mudWeight + mossWeight;
                alphas[zIndex, xIndex, 0] = grassWeight / sum;
                alphas[zIndex, xIndex, 1] = mudWeight / sum;
                alphas[zIndex, xIndex, 2] = mossWeight / sum;
            }
        }
        data.SetAlphamaps(0, 0, alphas);
        EditorUtility.SetDirty(data);

        GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.name = "Swamp Terrain";
        terrainObject.transform.position = Vector3.zero;

        // Use the URP terrain material, otherwise the terrain can appear pink
        Terrain createdTerrain = terrainObject.GetComponent<Terrain>();
        Material terrainMaterial = null;
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            terrainMaterial = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial;
        }
        if (terrainMaterial == null)
        {
            Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader != null) terrainMaterial = new Material(terrainShader);
        }
        if (terrainMaterial != null) createdTerrain.materialTemplate = terrainMaterial;
        return createdTerrain;
    }

    private static TerrainLayer CreateTerrainLayer(string name, Color colorA, Color colorB, int seed)
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name + "Texture" };
        Random.InitState(seed);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Tileable pattern: random grain + repeating waves
                float waves = 0.5f + 0.5f * Mathf.Sin(x / (float)size * Mathf.PI * 4f) * Mathf.Sin(y / (float)size * Mathf.PI * 6f);
                float noise = Random.value * 0.6f + waves * 0.4f;
                texture.SetPixel(x, y, Color.Lerp(colorA, colorB, noise));
            }
        }
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.Apply();
        AssetDatabase.CreateAsset(texture, $"{TerrainFolder}/{name}Texture.asset");

        TerrainLayer layer = new TerrainLayer
        {
            name = name,
            diffuseTexture = texture,
            tileSize = new Vector2(4f, 4f)
        };
        AssetDatabase.CreateAsset(layer, $"{TerrainFolder}/{name}.terrainlayer");
        return layer;
    }

    // =====================================================================
    // ProBuilder geometry (ProBuilder requirement)
    // =====================================================================

    /// <summary>Creates one ProBuilder mesh made of one or more boxes (given as min/max in local space).</summary>
    private static GameObject CreateProBuilderBoxes(string name, List<Vector3[]> boxes, Material material,
        Transform parent, Vector3 position, Vector3 rotation, bool addCollider)
    {
        List<Vector3> positions = new List<Vector3>();
        List<Face> faces = new List<Face>();
        foreach (Vector3[] box in boxes) AddBox(positions, faces, box[0], box[1]);

        ProBuilderMesh mesh = ProBuilderMesh.Create(positions, faces);
        mesh.ToMesh();
        mesh.Refresh();

        GameObject gameObject = mesh.gameObject;
        gameObject.name = name;
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.position = position;
        gameObject.transform.rotation = Quaternion.Euler(rotation);

        MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = material;

        if (addCollider)
        {
            MeshCollider collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
        }
        return gameObject;
    }

    private static GameObject CreateProBuilderBox(string name, Vector3 center, Vector3 size, Material material,
        Transform parent, Vector3 rotation = default, bool addCollider = true)
    {
        Vector3 half = size * 0.5f;
        List<Vector3[]> boxes = new List<Vector3[]> { new[] { -half, half } };
        return CreateProBuilderBoxes(name, boxes, material, parent, center, rotation, addCollider);
    }

    private static void AddBox(List<Vector3> positions, List<Face> faces, Vector3 min, Vector3 max)
    {
        Vector3[] corners =
        {
            new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
        };
        int[][] quads =
        {
            new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 },
            new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 }
        };
        Vector3[] outward = { Vector3.back, Vector3.forward, Vector3.left, Vector3.right, Vector3.up, Vector3.down };

        for (int i = 0; i < quads.Length; i++)
        {
            Vector3 a = corners[quads[i][0]];
            Vector3 b = corners[quads[i][1]];
            Vector3 c = corners[quads[i][2]];
            Vector3 d = corners[quads[i][3]];

            // Make sure the face points outwards (Unity: clockwise = front)
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward[i]) < 0f)
            {
                Vector3 temp = b;
                b = d;
                d = temp;
            }

            int start = positions.Count;
            positions.Add(a);
            positions.Add(b);
            positions.Add(c);
            positions.Add(d);
            faces.Add(new Face(new[] { start, start + 1, start + 2, start, start + 2, start + 3 }));
        }
    }

    /// <summary>
    /// Stairs that rise along +Z, starting at "bottomFront".
    /// They look like steps but have a smooth invisible ramp, so Clodi and the enemies can walk up.
    /// </summary>
    private static GameObject CreateStairs(string name, Vector3 bottomFront, float yaw, float width, float height,
        float depth, int steps, Material material, Transform parent)
    {
        List<Vector3[]> boxes = new List<Vector3[]>();
        for (int i = 0; i < steps; i++)
        {
            float z0 = depth * i / steps;
            float z1 = depth * (i + 1) / steps;
            float y1 = height * (i + 1) / steps;
            boxes.Add(new[] { new Vector3(-width * 0.5f, -0.3f, z0), new Vector3(width * 0.5f, y1, z1) });
        }
        GameObject stairs = CreateProBuilderBoxes(name, boxes, material, parent, bottomFront, new Vector3(0f, yaw, 0f), false);

        float length = Mathf.Sqrt(height * height + depth * depth);
        float angle = Mathf.Atan2(height, depth) * Mathf.Rad2Deg;
        Quaternion slope = Quaternion.Euler(-angle, 0f, 0f);

        GameObject ramp = new GameObject("WalkableRamp");
        ramp.transform.SetParent(stairs.transform, false);
        ramp.transform.localRotation = slope;
        ramp.transform.localPosition = new Vector3(0f, height * 0.5f, depth * 0.5f) - slope * Vector3.up * 0.05f;
        BoxCollider rampCollider = ramp.AddComponent<BoxCollider>();
        rampCollider.size = new Vector3(width, 0.1f, length + 0.1f);
        return stairs;
    }

    private static void BuildLevel(Transform level)
    {
        Material wood = Mat("Wood");
        Material darkWood = Mat("DarkWood");
        Material stone = Mat("Stone");

        // --- Bridge over the swamp pit ---
        Transform bridge = new GameObject("Bridge").transform;
        bridge.SetParent(level, false);
        CreateProBuilderBox("BridgeDeck", new Vector3(50f, 1.85f, 70f), new Vector3(22f, 0.4f, 3.5f), wood, bridge);
        CreateProBuilderBox("RailingLeft", new Vector3(50f, 2.5f, 68.35f), new Vector3(22f, 0.9f, 0.2f), darkWood, bridge);
        CreateProBuilderBox("RailingRight", new Vector3(50f, 2.5f, 71.65f), new Vector3(22f, 0.9f, 0.2f), darkWood, bridge);
        List<Vector3[]> supports = new List<Vector3[]>();
        foreach (float x in new[] { -4f, 4f })
        {
            foreach (float z in new[] { -1.5f, 1.5f })
            {
                supports.Add(new[] { new Vector3(x - 0.2f, 0f, z - 0.2f), new Vector3(x + 0.2f, 1.65f, z + 0.2f) });
            }
        }
        CreateProBuilderBoxes("BridgeSupports", supports, darkWood, bridge, new Vector3(50f, 0f, 70f), Vector3.zero, true);

        // --- Stepping stones (jump across the pit) ---
        Transform stones = new GameObject("SteppingStones").transform;
        stones.SetParent(level, false);
        foreach (float x in new[] { 46.5f, 50f, 53.5f })
        {
            CreateProBuilderBox("SteppingStone", new Vector3(x, 1.025f, 25f), new Vector3(2f, 2.05f, 2f), stone, stones);
        }

        // --- Watch tower with stairs (one egg on top) ---
        Transform tower = new GameObject("WatchTower").transform;
        tower.SetParent(level, false);
        Vector3 towerGround = Ground(30f, 60f);
        CreateProBuilderBox("TowerBlock", new Vector3(30f, towerGround.y + 1.75f, 60f), new Vector3(4f, 4.5f, 4f), stone, tower);
        CreateStairs("TowerStairs", new Vector3(30f, towerGround.y, 51f), 0f, 2.5f, 4f, 7f, 16, wood, tower);

        // --- Nest platform with stairs ---
        Transform nest = new GameObject("NestPlatform").transform;
        nest.SetParent(level, false);
        Vector3 nestGround = Ground(82f, 80f);
        CreateProBuilderBox("Platform", new Vector3(82f, nestGround.y + 0.35f, 80f), new Vector3(10f, 1.7f, 10f), wood, nest);
        CreateStairs("PlatformStairs", new Vector3(82f, nestGround.y, 72f), 0f, 3f, 1.2f, 3f, 6, darkWood, nest);
        Vector3 nestCenter = new Vector3(82f, nestGround.y + 1.2f, 80f);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1.4f;
            CreateProBuilderBox("NestTwig", nestCenter + offset + Vector3.up * 0.2f, new Vector3(1.2f, 0.4f, 0.35f),
                Mat("Nest"), nest, new Vector3(0f, angle + 90f, 8f), false);
        }
        GameObject bowl = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bowl.name = "NestBowl";
        bowl.transform.SetParent(nest, false);
        bowl.transform.position = nestCenter;
        bowl.transform.localScale = new Vector3(2.6f, 0.4f, 2.6f);
        bowl.GetComponent<MeshRenderer>().sharedMaterial = Mat("Nest");
        RemoveCollider(bowl);

        // --- Guard hut (one egg inside) ---
        Vector3 hutGround = Ground(68f, 55f);
        List<Vector3[]> hutParts = new List<Vector3[]>
        {
            new[] { new Vector3(-3f, -0.3f, 2.7f), new Vector3(3f, 3f, 3f) },     // back wall
            new[] { new Vector3(-3f, -0.3f, -3f), new Vector3(-2.7f, 3f, 3f) },   // left wall
            new[] { new Vector3(2.7f, -0.3f, -3f), new Vector3(3f, 3f, 3f) },     // right wall
            new[] { new Vector3(-3f, -0.3f, -3f), new Vector3(-1f, 3f, -2.7f) },  // front left
            new[] { new Vector3(1f, -0.3f, -3f), new Vector3(3f, 3f, -2.7f) },    // front right
            new[] { new Vector3(-1f, 2.2f, -3f), new Vector3(1f, 3f, -2.7f) },    // above the door
            new[] { new Vector3(-3.3f, 3f, -3.3f), new Vector3(3.3f, 3.3f, 3.3f) } // roof
        };
        CreateProBuilderBoxes("GuardHut", hutParts, darkWood, level, hutGround, Vector3.zero, true);

        // --- Decoration ---
        Transform decoration = new GameObject("Decoration").transform;
        decoration.SetParent(level, false);
        Random.InitState(42);

        // Rocks
        Vector2[] rockSpots = { new Vector2(12f, 30f), new Vector2(24f, 22f), new Vector2(36f, 45f), new Vector2(18f, 72f),
                                new Vector2(38f, 86f), new Vector2(64f, 40f), new Vector2(76f, 28f), new Vector2(66f, 84f) };
        foreach (Vector2 spot in rockSpots)
        {
            Vector3 size = new Vector3(Random.Range(0.8f, 2f), Random.Range(0.6f, 1.4f), Random.Range(0.8f, 2f));
            Vector3 rotation = new Vector3(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-10f, 10f));
            CreateProBuilderBox("Rock", Ground(spot.x, spot.y) + Vector3.up * (size.y * 0.35f), size, stone, decoration, rotation);
        }

        // Dead trees
        Vector2[] treeSpots = { new Vector2(10f, 55f), new Vector2(35f, 30f), new Vector2(75f, 35f), new Vector2(60f, 50f), new Vector2(25f, 82f) };
        foreach (Vector2 spot in treeSpots)
        {
            List<Vector3[]> tree = new List<Vector3[]>
            {
                new[] { new Vector3(-0.25f, -0.3f, -0.25f), new Vector3(0.25f, 4f, 0.25f) },
                new[] { new Vector3(0.25f, 2.4f, -0.1f), new Vector3(1.4f, 2.6f, 0.1f) },
                new[] { new Vector3(-1.1f, 3.1f, -0.1f), new Vector3(-0.25f, 3.3f, 0.1f) },
                new[] { new Vector3(-0.1f, 1.8f, 0.25f), new Vector3(0.1f, 2f, 1.1f) }
            };
            CreateProBuilderBoxes("DeadTree", tree, darkWood, decoration, Ground(spot.x, spot.y),
                new Vector3(0f, Random.Range(0f, 360f), 0f), true);
        }

        // Reeds along the pit
        List<Vector3[]> reeds = new List<Vector3[]>();
        for (int i = 0; i < 90; i++)
        {
            bool leftSide = i % 2 == 0;
            float x = leftSide ? Random.Range(40.5f, 44f) : Random.Range(56f, 59.5f);
            float z = Random.Range(4f, 96f);
            if (Mathf.Abs(z - 70f) < 3f || Mathf.Abs(z - 25f) < 2f) continue; // keep bridge and stones free
            Vector3 bottom = Ground(x, z) + Vector3.down * 0.1f;
            float height = Random.Range(0.8f, 1.8f);
            reeds.Add(new[] { bottom + new Vector3(-0.04f, 0f, -0.04f), bottom + new Vector3(0.04f, height, 0.04f) });
        }
        CreateProBuilderBoxes("Reeds", reeds, Mat("Reed"), decoration, Vector3.zero, Vector3.zero, false);

        // Mud water inside the pit + lily pads
        GameObject mudWater = GameObject.CreatePrimitive(PrimitiveType.Plane);
        mudWater.name = "MudWater";
        mudWater.transform.SetParent(decoration, false);
        mudWater.transform.position = new Vector3(50f, 0.9f, 50f);
        mudWater.transform.localScale = new Vector3(1.2f, 1f, 10f);
        mudWater.GetComponent<MeshRenderer>().sharedMaterial = Mat("MudWater");
        RemoveCollider(mudWater);

        for (int i = 0; i < 14; i++)
        {
            GameObject lily = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lily.name = "LilyPad";
            lily.transform.SetParent(decoration, false);
            lily.transform.position = new Vector3(Random.Range(46.5f, 53.5f), 0.93f, Random.Range(5f, 95f));
            float scale = Random.Range(0.6f, 1.3f);
            lily.transform.localScale = new Vector3(scale, 0.02f, scale);
            lily.GetComponent<MeshRenderer>().sharedMaterial = Mat("LilyPad");
            RemoveCollider(lily);
        }

        // Invisible borders so nobody walks off the map
        Transform borders = new GameObject("MapBorders").transform;
        borders.SetParent(level, false);
        CreateBorder(borders, new Vector3(50f, 15f, -0.5f), new Vector3(100f, 40f, 1f));
        CreateBorder(borders, new Vector3(50f, 15f, 100.5f), new Vector3(100f, 40f, 1f));
        CreateBorder(borders, new Vector3(-0.5f, 15f, 50f), new Vector3(1f, 40f, 100f));
        CreateBorder(borders, new Vector3(100.5f, 15f, 50f), new Vector3(1f, 40f, 100f));
    }

    private static void CreateBorder(Transform parent, Vector3 center, Vector3 size)
    {
        GameObject border = new GameObject("Border");
        border.transform.SetParent(parent, false);
        border.transform.position = center;
        BoxCollider collider = border.AddComponent<BoxCollider>();
        collider.size = size;
    }

    // =====================================================================
    // NavMesh (AI requirement)
    // =====================================================================

    private static void BakeNavMesh()
    {
        GameObject navMeshObject = new GameObject("NavMesh");
        NavMeshSurface surface = navMeshObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        // Enemies must not walk into the swamp pit
        GameObject pitBlocker = new GameObject("PitBlocker (Not Walkable)");
        pitBlocker.transform.SetParent(navMeshObject.transform, false);
        pitBlocker.transform.position = new Vector3(50f, 0.4f, 50f);
        NavMeshModifierVolume volume = pitBlocker.AddComponent<NavMeshModifierVolume>();
        volume.center = Vector3.zero;
        volume.size = new Vector3(PitMaxX - PitMinX, 2.8f, MapSize);
        volume.area = NavMesh.GetAreaFromName("Not Walkable");

        Physics.SyncTransforms();
        surface.BuildNavMesh();

        if (surface.navMeshData != null)
        {
            AssetDatabase.CreateAsset(surface.navMeshData, GeneratedFolder + "/SwampNavMesh.asset");
            EditorUtility.SetDirty(surface);
        }
        else
        {
            Debug.LogWarning("NavMesh could not be baked. Select 'NavMesh' in the Hierarchy and press 'Bake'.");
        }
    }

    // =====================================================================
    // Prefabs
    // =====================================================================

    private static GameObject CreateMudBallPrefab()
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "MudBall";
        ball.transform.localScale = Vector3.one * 0.35f;
        ball.GetComponent<MeshRenderer>().sharedMaterial = Mat("MudBall");
        Rigidbody body = ball.AddComponent<Rigidbody>();
        body.mass = 0.3f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ball.AddComponent<MudBall>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(ball, PrefabFolder + "/MudBall.prefab");
        Object.DestroyImmediate(ball);
        return prefab;
    }

    /// <summary>Creates a base collectible prefab and a "Swamp Egg" prefab variant of it.</summary>
    private static GameObject CreateEggPrefabs()
    {
        GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        baseObject.name = "Collectible_Base";
        baseObject.transform.localScale = Vector3.one * 0.6f;
        baseObject.GetComponent<MeshRenderer>().sharedMaterial = Mat("EggBase");
        baseObject.GetComponent<SphereCollider>().isTrigger = true;
        baseObject.AddComponent<Collectible>();
        GameObject basePrefab = PrefabUtility.SaveAsPrefabAsset(baseObject, PrefabFolder + "/Collectible_Base.prefab");
        Object.DestroyImmediate(baseObject);

        // Prefab Variant: inherits from the base, overrides look
        GameObject variantObject = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        variantObject.name = "Collectible_SwampEgg";
        variantObject.transform.localScale = new Vector3(0.55f, 0.75f, 0.55f);
        variantObject.GetComponent<MeshRenderer>().sharedMaterial = Mat("EggGlow");

        GameObject glow = new GameObject("Glow");
        glow.transform.SetParent(variantObject.transform, false);
        Light light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.5f, 1f, 0.4f);
        light.range = 3f;
        light.intensity = 2f;

        GameObject variantPrefab = PrefabUtility.SaveAsPrefabAsset(variantObject, PrefabFolder + "/Collectible_SwampEgg.prefab");
        Object.DestroyImmediate(variantObject);
        return variantPrefab;
    }

    private static GameObject CreateEnemyPrefab()
    {
        GameObject root = new GameObject("SwampGuard");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        body.GetComponent<MeshRenderer>().sharedMaterial = Mat("Guard");
        RemoveCollider(body);

        foreach (float side in new[] { -0.2f, 0.2f })
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = new Vector3(side, 1.5f, 0.42f);
            eye.transform.localScale = Vector3.one * 0.2f;
            eye.GetComponent<MeshRenderer>().sharedMaterial = Mat("GuardEyes");
            RemoveCollider(eye);
        }

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 1f, 0f);
        collider.height = 2f;
        collider.radius = 0.5f;

        Rigidbody rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.speed = 2f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1.2f;
        agent.radius = 0.5f;
        agent.height = 2f;

        root.AddComponent<EnemyStatus>();
        EnemyAI ai = root.AddComponent<EnemyAI>();
        SetField(ai, "bodyRenderer", body.GetComponent<MeshRenderer>());

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/SwampGuard.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // =====================================================================
    // Player
    // =====================================================================

    private static GameObject CreatePlayer(Vector3 position, GameObject mudBallPrefab)
    {
        GameObject player = new GameObject("Player");
        player.tag = "Player";
        player.transform.position = position;

        // Visual model (Clodi), as a child
        GameObject model = null;
        GameObject modelAsset = FindAsset<GameObject>("Clodi_Project_Talotta t:Model", "Clodi_Project_Talotta");
        if (modelAsset != null)
        {
            model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        }
        else
        {
            Debug.LogWarning("Clodi model not found. Using a placeholder capsule. Import clodi.unitypackage first.");
            model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.GetComponent<MeshRenderer>().sharedMaterial = Mat("Placeholder");
        }
        model.name = "ClodiModel";
        model.transform.SetParent(player.transform, false);
        foreach (Collider modelCollider in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(modelCollider);
        FitModel(model.transform, player.transform, 1.5f);

        Animator animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        RuntimeAnimatorController controller = FindAsset<RuntimeAnimatorController>("Clodi_Controller t:AnimatorController", "Clodi_Controller");
        if (controller != null) animator.runtimeAnimatorController = controller;
        else Debug.LogWarning("Clodi_Controller not found. Animations will not play.");
        animator.applyRootMotion = false;

        // Physics
        Rigidbody body = player.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
        capsule.height = 1.5f;
        capsule.radius = 0.4f;
        capsule.center = new Vector3(0f, 0.75f, 0f);

        PhysicsMaterial noFriction = new PhysicsMaterial("NoFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
        AssetDatabase.CreateAsset(noFriction, MaterialFolder + "/NoFriction.asset");
        capsule.sharedMaterial = noFriction;

        // Scripts
        PlayerController controllerScript = player.AddComponent<PlayerController>();
        player.AddComponent<PlayerHealth>();

        SetField(controllerScript, "moveAction", FindActionReference("Move"));
        SetField(controllerScript, "jumpAction", FindActionReference("Jump"));
        SetField(controllerScript, "attackAction", FindActionReference("Attack"));
        SetField(controllerScript, "mudBallPrefab", mudBallPrefab);
        SetField(controllerScript, "animator", animator);

        PrefabUtility.SaveAsPrefabAssetAndConnect(player, PrefabFolder + "/Player.prefab", InteractionMode.AutomatedAction);
        return player;
    }

    /// <summary>Scales the model to a target height and puts its feet on the player's origin.</summary>
    private static void FitModel(Transform model, Transform root, float targetHeight)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = GetBounds(renderers);
        if (bounds.size.y > 0.001f) model.localScale *= targetHeight / bounds.size.y;

        bounds = GetBounds(renderers);
        Vector3 offset = new Vector3(bounds.center.x - root.position.x, bounds.min.y - root.position.y, bounds.center.z - root.position.z);
        model.position -= offset;
    }

    private static Bounds GetBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static InputActionReference FindActionReference(string actionName)
    {
        HashSet<string> paths = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:InputActionAsset")) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string guid in AssetDatabase.FindAssets("InputSystem_Actions"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".inputactions")) paths.Add(path);
        }

        foreach (string path in paths)
        {
            foreach (InputActionReference reference in AssetDatabase.LoadAllAssetsAtPath(path).OfType<InputActionReference>())
            {
                InputAction action = reference.action;
                if (action != null && action.name == actionName && action.actionMap != null && action.actionMap.name == "Player")
                {
                    return reference;
                }
            }
        }

        Debug.LogWarning($"Input action 'Player/{actionName}' not found. Assign it manually on the PlayerController.");
        return null;
    }

    // =====================================================================
    // Eggs, zones, enemies
    // =====================================================================

    private static void PlaceEggs(GameObject eggPrefab, Transform parent)
    {
        Transform eggs = new GameObject("Eggs").transform;
        eggs.SetParent(parent, false);

        Vector3[] spots =
        {
            Ground(22f, 40f) + Vector3.up * 0.8f,               // start side, easy
            Ground(30f, 60f) + Vector3.up * 4.8f,               // on top of the watch tower
            Ground(66f, 25f) + Vector3.up * 0.8f,               // after the stepping stones
            Ground(68f, 55f) + Vector3.up * 0.8f,               // inside the guard hut
            Ground(60f, 88f) + Vector3.up * 0.8f                // after the bridge
        };

        foreach (Vector3 spot in spots)
        {
            GameObject egg = (GameObject)PrefabUtility.InstantiatePrefab(eggPrefab);
            egg.transform.SetParent(eggs, false);
            egg.transform.position = spot;
        }
    }

    private static void CreateZones(Transform parent, GameObject nestHint)
    {
        // Swamp pit: falling in = lose
        GameObject pit = new GameObject("SwampPit_DeathZone");
        pit.transform.SetParent(parent, false);
        pit.transform.position = new Vector3(50f, 0.6f, 50f);
        BoxCollider pitCollider = pit.AddComponent<BoxCollider>();
        pitCollider.isTrigger = true;
        pitCollider.size = new Vector3(PitMaxX - PitMinX, 1.2f, MapSize);
        pit.AddComponent<DeathZone>();

        // Safety net far below the map
        GameObject killFloor = new GameObject("KillFloor_DeathZone");
        killFloor.transform.SetParent(parent, false);
        killFloor.transform.position = new Vector3(50f, -10f, 50f);
        BoxCollider killCollider = killFloor.AddComponent<BoxCollider>();
        killCollider.isTrigger = true;
        killCollider.size = new Vector3(200f, 2f, 200f);
        killFloor.AddComponent<DeathZone>();

        // Nest: bring all eggs here = win
        Vector3 nestGround = Ground(82f, 80f);
        GameObject nest = new GameObject("Nest_WinZone");
        nest.transform.SetParent(parent, false);
        nest.transform.position = new Vector3(82f, nestGround.y + 2.2f, 80f);
        BoxCollider nestCollider = nest.AddComponent<BoxCollider>();
        nestCollider.isTrigger = true;
        nestCollider.size = new Vector3(3.5f, 2f, 3.5f);
        WinZone winZone = nest.AddComponent<WinZone>();
        SetField(winZone, "notEnoughEggsHint", nestHint);
    }

    private static void PlaceEnemies(GameObject enemyPrefab)
    {
        Transform enemies = new GameObject("Enemies").transform;

        Vector2[] routeA = { new Vector2(62f, 18f), new Vector2(80f, 18f), new Vector2(80f, 40f), new Vector2(62f, 40f) };
        Vector2[] routeB = { new Vector2(60f, 66f), new Vector2(74f, 66f), new Vector2(74f, 90f), new Vector2(60f, 90f) };

        CreateEnemy(enemyPrefab, enemies, "SwampGuard_South", routeA);
        CreateEnemy(enemyPrefab, enemies, "SwampGuard_North", routeB);
    }

    private static void CreateEnemy(GameObject prefab, Transform parent, string name, Vector2[] route)
    {
        Transform waypointParent = new GameObject(name + "_Waypoints").transform;
        waypointParent.SetParent(parent, false);

        Transform[] waypoints = new Transform[route.Length];
        for (int i = 0; i < route.Length; i++)
        {
            Transform point = new GameObject("Waypoint_" + (i + 1)).transform;
            point.SetParent(waypointParent, false);
            point.position = Ground(route[i].x, route[i].y);
            waypoints[i] = point;
        }

        GameObject enemy = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        enemy.name = name;
        enemy.transform.SetParent(parent, false);
        enemy.transform.position = waypoints[0].position;
        SetObjectArray(enemy.GetComponent<EnemyAI>(), "patrolPoints", waypoints);
    }

    // =====================================================================
    // UI and GameManager
    // =====================================================================

    private struct UiReferences
    {
        public Text eggCounter;
        public Text health;
        public GameObject pauseScreen;
        public GameObject winScreen;
        public GameObject loseScreen;
        public GameObject nestHint;
    }

    private static UiReferences CreateUi()
    {
        GameObject canvasObject = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasObject.transform;

        UiReferences ui = new UiReferences();
        ui.eggCounter = CreateText(root, "EggCounter", "Eggs: 0/" + EggCount, 44, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(600f, 60f));
        ui.health = CreateText(root, "Health", "Health: 3/3", 44, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(40f, -90f), new Vector2(600f, 60f));
        CreateText(root, "Goal", $"Collect all {EggCount} swamp eggs and bring them to Clodi's nest!", 30, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1100f, 50f));
        CreateText(root, "Controls", "WASD Move   ·   Space Jump   ·   Left Click Throw mud   ·   Esc Pause", 26, TextAnchor.LowerLeft,
            new Vector2(0f, 0f), new Vector2(40f, 30f), new Vector2(1400f, 50f));

        Text hint = CreateText(root, "NestHint", $"You need all {EggCount} eggs first!", 52, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1200f, 80f));
        hint.gameObject.SetActive(false);
        ui.nestHint = hint.gameObject;

        ui.pauseScreen = CreatePanel(root, "PauseScreen", new Color(0f, 0f, 0f, 0.6f), "Paused", "Press Esc to continue");
        ui.winScreen = CreatePanel(root, "WinScreen", new Color(0.1f, 0.35f, 0.1f, 0.75f), "You did it!", "All eggs are safe in Clodi's nest.");
        ui.loseScreen = CreatePanel(root, "LoseScreen", new Color(0.35f, 0.08f, 0.05f, 0.75f), "Oh no!", "Clodi didn't make it. Restarting...");
        return ui;
    }

    private static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor alignment,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = textObject.AddComponent<Text>();
        text.font = uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        Outline outline = textObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static GameObject CreatePanel(Transform parent, string name, Color background, string title, string subtitle)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = background;

        CreateText(panel.transform, "Title", title, 110, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(1600f, 160f));
        CreateText(panel.transform, "Subtitle", subtitle, 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(1600f, 80f));
        panel.SetActive(false);
        return panel;
    }

    private static void CreateGameManager(UiReferences ui)
    {
        GameObject managerObject = new GameObject("GameManager");
        GameManager manager = managerObject.AddComponent<GameManager>();
        SetInt(manager, "eggsToCollect", EggCount);
        SetField(manager, "eggCounterText", ui.eggCounter);
        SetField(manager, "healthText", ui.health);
        SetField(manager, "pauseScreen", ui.pauseScreen);
        SetField(manager, "winScreen", ui.winScreen);
        SetField(manager, "loseScreen", ui.loseScreen);
    }

    // =====================================================================
    // Camera, light, saving
    // =====================================================================

    private static void SetupCameraAndLight(Transform player)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
        }
        camera.farClipPlane = 300f;
        CameraFollow follow = camera.gameObject.AddComponent<CameraFollow>();
        SetField(follow, "target", player);
        camera.transform.position = player.position + new Vector3(0f, 6f, -8f);
        camera.transform.LookAt(player.position + Vector3.up);

        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            light.color = new Color(1f, 0.93f, 0.8f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        }

        // Swamp atmosphere
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.012f;
        RenderSettings.fogColor = new Color(0.55f, 0.62f, 0.5f);
    }

    private static void SaveSceneAndBuildSettings(Scene scene)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        List<EditorBuildSettingsScene> buildScenes = EditorBuildSettings.scenes
            .Where(s => s.path != ScenePath)
            .ToList();
        buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
#endif
