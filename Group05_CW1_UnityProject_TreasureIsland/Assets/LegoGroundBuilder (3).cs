#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Import anywhere inside Assets. Opens from Tools > LEGO Ground Builder.
// Generates saved scene objects in Edit mode, not temporary Play-mode objects.
public class LegoGroundBuilder : EditorWindow
{
    [SerializeField] private GameObject tileModel;
    [SerializeField] private Material sandMaterial;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private float beachWidth = 8f;
    [SerializeField] private GameObject[] surfaces = new GameObject[0];
    [SerializeField] private float tileSize = 4f;
    [SerializeField] private bool addRiver = false;
    [SerializeField] private float riverWidth = 16f;
    [SerializeField] private float riverBend = 20f;
    [SerializeField] private float waterDrop = 0.35f;
    [SerializeField] private float flowSpeed = 2f;
    [SerializeField] private GameObject generatedRoot;
    private string status = "Choose a flat, square LEGO plate from the Project window.";

    [MenuItem("Tools/LEGO Ground Builder")]
    public static void OpenWindow()
    {
        GetWindow<LegoGroundBuilder>("LEGO Ground");
    }

    private void OnEnable()
    {
        if (surfaces == null || surfaces.Length == 0)
        {
            var found = new List<GameObject>();
            foreach (string name in new[] { "round island", "Round Island", "Island Centre", "Island_Centre", "Island_Left", "Island_Right" })
            {
                GameObject go = GameObject.Find(name);
                if (go != null && HasSurfaceGeometry(go))
                    found.Add(go);
            }
            surfaces = found.ToArray();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("LEGO Sand Ground", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Supports sand cubes and round Cylinder islands with Mesh Colliders. Select your island, then click Use Selected Sand Objects. Use a flat LEGO plate model from Project. Turn Add River off for Cylinder islands.", MessageType.Info);
        tileModel = (GameObject)EditorGUILayout.ObjectField("LEGO Plate", tileModel, typeof(GameObject), false);
        sandMaterial = (Material)EditorGUILayout.ObjectField("Sand Material", sandMaterial, typeof(Material), false);
        grassMaterial = (Material)EditorGUILayout.ObjectField("Green Ground Material", grassMaterial, typeof(Material), false);
        if (GUILayout.Button("Create Green Material")) CreateGreenMaterial();
        beachWidth = Mathf.Max(0f, EditorGUILayout.FloatField("Beach Width", beachWidth));
        EditorGUILayout.LabelField("Green material fills the centre. Beach Width sets the sand strip at the sea edge in world units.", EditorStyles.wordWrappedLabel);
        tileSize = Mathf.Clamp(EditorGUILayout.FloatField("Tile Size", tileSize), 1f, 100f);
        addRiver = EditorGUILayout.Toggle("Add River", addRiver);
        if (addRiver)
        {
            riverWidth = Mathf.Max(2f, EditorGUILayout.FloatField("River Width", riverWidth));
            riverBend = Mathf.Max(0f, EditorGUILayout.FloatField("River Bend", riverBend));
            waterDrop = Mathf.Max(0.05f, EditorGUILayout.FloatField("Water Below Ground", waterDrop));
            flowSpeed = EditorGUILayout.Slider("Flow Speed", flowSpeed, 0f, 8f);
            EditorGUILayout.HelpBox("Select only Island Centre. This keeps the square island and cuts a winding river through it along world Z. After generating, untick the original cube's Mesh Renderer AND Box Collider; leave its GameObject active. Play to see the animated water. Supports Built-in and URP.", MessageType.Info);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sand surfaces: " + (surfaces == null ? 0 : surfaces.Length));
        if (surfaces != null)
            foreach (GameObject surface in surfaces)
                EditorGUILayout.ObjectField(surface, typeof(GameObject), true);
        if (GUILayout.Button("Use Selected Sand Objects"))
        {
            var selected = new List<GameObject>();
            foreach (GameObject go in Selection.gameObjects)
                if (HasSurfaceGeometry(go) && !EditorUtility.IsPersistent(go))
                    selected.Add(go);
            surfaces = selected.ToArray();
            status = "Selected " + surfaces.Length + " sand surface(s).";
        }
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("GENERATE LEGO GROUND", GUILayout.Height(36))) Generate();
            if (GUILayout.Button("Remove Generated Ground")) RemoveGenerated();
        }
        EditorGUILayout.HelpBox(status, MessageType.Info);
        EditorGUILayout.LabelField("Generated tiles stay in the scene. Save with Ctrl+S.", EditorStyles.wordWrappedLabel);
    }

    private void Generate()
    {
        if (tileModel == null || !EditorUtility.IsPersistent(tileModel))
        {
            status = "Drag a LEGO plate MODEL from the Project window into LEGO Plate.";
            return;
        }
        if (surfaces == null || surfaces.Length == 0)
        {
            status = "Select your sand cube or round Cylinder in Hierarchy, then click Use Selected Sand Objects.";
            return;
        }

        var areas = new List<Bounds>();
        var areaSources = new List<GameObject>();
        var footprints = new List<Vector2[]>();
        var unique = new HashSet<GameObject>();
        Scene targetScene = default(Scene);
        int total = 0;
        float size = Mathf.Clamp(tileSize, 1f, 100f);
        foreach (GameObject surface in surfaces)
        {
            if (surface == null || !unique.Add(surface)) continue;
            if (!surface.activeInHierarchy || !HasSurfaceGeometry(surface)) continue;
            BoxCollider box = surface.GetComponent<BoxCollider>();
            bool isRound = IsCylinder(surface);
            Collider sourceCollider = isRound ? (Collider)surface.GetComponent<MeshCollider>() : box;
            if (sourceCollider == null || sourceCollider.isTrigger) continue;
            if (Quaternion.Angle(surface.transform.rotation, Quaternion.identity) > 0.1f)
            {
                status = "Island surfaces must have world Rotation 0,0,0. Nothing was changed.";
                return;
            }
            if (!targetScene.IsValid()) targetScene = surface.scene;
            if (surface.scene != targetScene)
            {
                status = "Choose sand surfaces from the same scene.";
                return;
            }
            // Collider.bounds is empty when disabled. Read the local box geometry
            // instead, so the original square can stay hidden during regeneration.
            Vector3 scale = surface.transform.lossyScale;
            if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
            {
                status = "Use positive island scales. Nothing was changed.";
                return;
            }
            Bounds localBounds = isRound ? surface.GetComponent<MeshFilter>().sharedMesh.bounds
                : new Bounds(box.center, box.size);
            Bounds b = new Bounds(surface.transform.TransformPoint(localBounds.center),
                Vector3.Scale(localBounds.size, scale));
            Vector2[] footprint = isRound ? CylinderFootprint(surface) : null;
            if (isRound && (footprint == null || footprint.Length < 3))
            {
                status = "Could not read the Cylinder footprint. Nothing was changed.";
                return;
            }
            if (isRound && addRiver)
            {
                status = "For round Cylinder islands, untick Add River first. River mode currently uses a cube base.";
                return;
            }
            if (b.size.x < 0.01f || b.size.z < 0.01f) continue;
            areas.Add(b);
            areaSources.Add(surface);
            footprints.Add(footprint);
            int columns = Mathf.CeilToInt(b.size.x / size);
            int rows = Mathf.CeilToInt(b.size.z / size);
            if ((long)columns * rows > 100000)
            {
                status = "Surface is too large. Increase Tile Size. Nothing was changed.";
                return;
            }
            for (int z = 0; z < rows; z++)
                for (int x = 0; x < columns; x++)
                    if (KeepTile(x, z, columns, rows, b, footprint)) total++;
        }
        if (areas.Count == 0 || total == 0 || total > 2500)
        {
            status = areas.Count == 0 || total == 0 ? "No usable land tiles. Check the surface and river settings." : "Too many tiles. Increase Tile Size (try 8, 10 or 20 for a large island).";
            return;
        }

        if (addRiver && areas.Count != 1)
        {
            status = "For the river, select only the main Island Centre cube.";
            return;
        }
        if (addRiver && (riverWidth + 2f * EffectiveBend(areas[0]) + 4f * size >= areas[0].size.x))
        {
            status = "River is too wide for this island. Reduce River Width, River Bend or Tile Size.";
            return;
        }

        GameObject probe = Instantiate(tileModel);
        probe.hideFlags = HideFlags.HideAndDontSave;
        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        probe.transform.localScale = Vector3.one;
        Bounds original;
        bool valid = TryBounds(probe, out original);
        DestroyImmediate(probe);
        if (!valid || original.size.x < 0.0001f || original.size.z < 0.0001f || original.size.y < 0.0001f)
        {
            status = "This model has no usable visible mesh. Choose the full plate model.";
            return;
        }

        Material riverMaterial = null;
        if (addRiver)
        {
            try { riverMaterial = CreateWaterMaterial(areas[0]); }
            catch (System.Exception exception)
            {
                status = "Could not prepare river water: " + exception.Message + " Ground was not changed.";
                return;
            }
        }

        // Recover our named group after the tool window has been closed and reopened.
        // Only recognize a group containing this generator's named tile children.
        if (generatedRoot != null && generatedRoot.scene != targetScene) generatedRoot = null;
        if (generatedRoot == null)
            foreach (GameObject root in targetScene.GetRootGameObjects())
            {
                if (root.name != "LEGO Sand Tiles (Generated)" || root.transform.childCount == 0) continue;
                bool owned = true;
                foreach (Transform child in root.transform)
                    if (!child.name.StartsWith("Sand Plate ")) { owned = false; break; }
                if (owned) { generatedRoot = root; break; }
            }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate LEGO Sand Ground");
        if (generatedRoot != null) Undo.DestroyObjectImmediate(generatedRoot);
        generatedRoot = new GameObject("LEGO Sand Tiles (Generated)");
        SceneManager.MoveGameObjectToScene(generatedRoot, targetScene);

        int created = 0;
        for (int areaIndex = 0; areaIndex < areas.Count; areaIndex++)
        {
            Bounds area = areas[areaIndex];
            GameObject source = areaSources[areaIndex];
            int columns = Mathf.CeilToInt(area.size.x / size);
            int rows = Mathf.CeilToInt(area.size.z / size);
            float width = area.size.x / columns;
            float depth = area.size.z / rows;
            for (int z = 0; z < rows; z++)
            {
                int runStart = -1;
                for (int x = 0; x < columns; x++)
                {
                    bool keep = KeepTile(x, z, columns, rows, area, footprints[areaIndex]);
                    if (addRiver && runStart >= 0 && !keep)
                    {
                        CreateFoundation(area, width, depth, runStart, x, z, source);
                        runStart = -1;
                    }
                    if (!keep) continue;
                    if (runStart < 0) runStart = x;
                    GameObject tile = Instantiate(tileModel, generatedRoot.transform);
                    tile.name = "Sand Plate " + x + "," + z;
                    created++;
                    tile.SetActive(true);
                    foreach (Transform part in tile.GetComponentsInChildren<Transform>(true))
                        part.gameObject.layer = source.layer;
                    float scaleX = width / original.size.x;
                    float scaleZ = depth / original.size.z;
                    // Keep thin plates thin; cap total visual height at 0.2 world units.
                    float scaleY = Mathf.Min(Mathf.Min(scaleX, scaleZ), 0.2f / original.size.y);
                    tile.transform.localRotation = Quaternion.identity;
                    tile.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);
                    Vector3 centre = new Vector3(area.min.x + (x + 0.5f) * width,
                        area.max.y, area.min.z + (z + 0.5f) * depth);
                    tile.transform.position = centre - new Vector3(original.center.x * scaleX,
                        original.min.y * scaleY, original.center.z * scaleZ);

                    // One simple static collider per tile, including its studs.
                    foreach (Rigidbody rb in tile.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
                    foreach (Collider collider in tile.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
                    Bounds rendered;
                    if (TryBounds(tile, out rendered))
                    {
                        BoxCollider collider = tile.AddComponent<BoxCollider>();
                        collider.center = tile.transform.InverseTransformPoint(rendered.center);
                        collider.size = new Vector3(rendered.size.x / scaleX,
                            rendered.size.y / scaleY, rendered.size.z / scaleZ);
                    }
                    Material groundMaterial = grassMaterial != null
                        && IsInlandTile(x, z, columns, rows, area, footprints[areaIndex])
                        ? grassMaterial : sandMaterial;
                    if (groundMaterial != null)
                        foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
                        {
                            Material[] materials = renderer.sharedMaterials;
                            for (int i = 0; i < materials.Length; i++) materials[i] = groundMaterial;
                            renderer.sharedMaterials = materials;
                        }
                }
                if (addRiver && runStart >= 0)
                    CreateFoundation(area, width, depth, runStart, columns, z, source);
            }
        }
        if (addRiver) CreateRiverWater(areas[0], riverMaterial);
        Undo.RegisterCreatedObjectUndo(generatedRoot, "Generate LEGO Sand Ground");
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(targetScene);
        status = "Created " + created + " tiles. " + (addRiver
            ? "River ready. Untick Island Centre's Mesh Renderer AND Box Collider; leave its GameObject active. Play to see water flow. "
            : "Keep the selected island's Mesh Renderer and ground collider enabled. ")
            + "Save with Ctrl+S. Ctrl+Z undoes generation.";
        SceneView.RepaintAll();
    }

    private void RemoveGenerated()
    {
        if (generatedRoot == null) { status = "No generated ground from this window to remove."; return; }
        Scene scene = generatedRoot.scene;
        Undo.DestroyObjectImmediate(generatedRoot);
        generatedRoot = null;
        EditorSceneManager.MarkSceneDirty(scene);
        status = "Generated tiles removed. Re-enable your source island's Mesh Renderer and ground collider if you hid them.";
    }

    private void CreateGreenMaterial()
    {
        bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
        Shader shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
        if (shader == null)
        {
            status = "Could not find a ground shader. Create a green material in Project and drag it into Green Ground Material.";
            return;
        }
        Material material = new Material(shader);
        Color green = new Color(0.22f, 0.50f, 0.12f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", green);
        if (material.HasProperty("_Color")) material.SetColor("_Color", green);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Materials/GreenGround.mat");
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        grassMaterial = material;
        status = "Green material created and assigned. Click GENERATE LEGO GROUND to apply the green centre and sandy beach.";
    }

    private bool IsInlandTile(int x, int z, int columns, int rows, Bounds area, Vector2[] footprint)
    {
        float width = area.size.x / columns;
        float depth = area.size.z / rows;
        float left = area.min.x + x * width;
        float bottom = area.min.z + z * depth;
        // A whole plate must be inland. Plates crossing the beach boundary
        // stay sandy, so the shoreline always has a continuous sand strip.
        return ShoreDistance(new Vector2(left, bottom), area, footprint) >= beachWidth
            && ShoreDistance(new Vector2(left + width, bottom), area, footprint) >= beachWidth
            && ShoreDistance(new Vector2(left, bottom + depth), area, footprint) >= beachWidth
            && ShoreDistance(new Vector2(left + width, bottom + depth), area, footprint) >= beachWidth;
    }

    private static float ShoreDistance(Vector2 point, Bounds area, Vector2[] footprint)
    {
        if (footprint == null)
            return Mathf.Min(Mathf.Min(point.x - area.min.x, area.max.x - point.x),
                Mathf.Min(point.y - area.min.z, area.max.z - point.y));
        float distance = float.PositiveInfinity;
        for (int i = 0; i < footprint.Length; i++)
        {
            Vector2 a = footprint[i];
            Vector2 b = footprint[(i + 1) % footprint.Length];
            float length = (b - a).magnitude;
            if (length > 0.000001f) distance = Mathf.Min(distance, Cross(a, b, point) / length);
        }
        return distance;
    }

    private static bool IsCylinder(GameObject go)
    {
        MeshFilter filter = go.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null
            && string.Equals(filter.sharedMesh.name, "Cylinder", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSurfaceGeometry(GameObject go)
    {
        if (IsCylinder(go)) return go.GetComponent<MeshCollider>() != null;
        return go.GetComponent<BoxCollider>() != null;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private static Vector2[] CylinderFootprint(GameObject go)
    {
        Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
        var points = new List<Vector2>();
        var unique = new HashSet<Vector2>();
        foreach (Vector3 vertex in mesh.vertices)
        {
            Vector3 world = go.transform.TransformPoint(vertex);
            Vector2 point = new Vector2(world.x, world.z);
            if (unique.Add(point)) points.Add(point);
        }
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        if (points.Count < 3) return null;
        // Convex hull of the real mesh vertices handles the primitive's flat
        // polygonal edges as well as oval Cylinders with unequal X/Z scales.
        var hull = new List<Vector2>();
        foreach (Vector2 point in points)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        int lowerCount = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            Vector2 point = points[i];
            while (hull.Count > lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }

    private static bool InsideFootprint(Vector2 point, Vector2[] footprint)
    {
        for (int i = 0; i < footprint.Length; i++)
            if (Cross(footprint[i], footprint[(i + 1) % footprint.Length], point) < -0.0001f)
                return false;
        return true;
    }

    private float EffectiveBend(Bounds area)
    {
        return Mathf.Min(riverBend, area.size.x * 0.25f);
    }

    private static float CentreAt(float t, float bend)
    {
        return bend * (0.72f * Mathf.Sin(t * Mathf.PI * 2f)
            + 0.28f * Mathf.Sin(t * Mathf.PI * 4f + 0.7f));
    }

    private bool KeepTile(int x, int z, int columns, int rows, Bounds area, Vector2[] footprint)
    {
        float width = area.size.x / columns;
        float depth = area.size.z / rows;
        if (footprint != null)
        {
            float left = area.min.x + x * width;
            float bottom = area.min.z + z * depth;
            // Test the whole plate, not only its centre. This prevents its
            // corners hanging past the actual polygonal Cylinder shoreline.
            if (!InsideFootprint(new Vector2(left, bottom), footprint)
                || !InsideFootprint(new Vector2(left + width, bottom), footprint)
                || !InsideFootprint(new Vector2(left, bottom + depth), footprint)
                || !InsideFootprint(new Vector2(left + width, bottom + depth), footprint)) return false;
        }
        if (!addRiver) return true;
        float worldX = area.min.x + (x + 0.5f) * width;
        float centre = area.center.x + CentreAt((z + 0.5f) / rows, EffectiveBend(area));
        // A bound on the centreline derivative guarantees the whole river
        // corridor stays open through each row, including sharp bends.
        float maxSlope = EffectiveBend(area) * Mathf.PI * 2f * 1.28f / area.size.z;
        float margin = width * 0.5f + maxSlope * depth * 0.5f;
        return Mathf.Abs(worldX - centre) > riverWidth * 0.5f + margin;
    }

    private void CreateRiverWater(Bounds area, Material material)
    {
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Sand Plate River Water";
        water.transform.SetParent(generatedRoot.transform, false);
        water.transform.position = new Vector3(area.center.x, area.max.y - waterDrop, area.center.z);
        water.transform.localScale = new Vector3(area.size.x / 10f, 1f, area.size.z / 10f);
        // Water is visual only; the character cannot stand on it.
        DestroyImmediate(water.GetComponent<Collider>());
        Renderer renderer = water.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private Material CreateWaterMaterial(Bounds area)
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        bool urp = pipeline != null && pipeline.GetType().Name.Contains("Universal");
        if (pipeline != null && !urp)
            throw new System.InvalidOperationException("This tool supports Built-in and URP projects.");
        const string folder = "Assets/LEGO River Generated";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "LEGO River Generated");
        string shaderPath = folder + (urp ? "/LegoRiverFlow_URP.shader" : "/LegoRiverFlow_BuiltIn.shader");
        string shaderText = WaterShader(urp);
        if (!File.Exists(shaderPath) || File.ReadAllText(shaderPath) != shaderText)
            File.WriteAllText(shaderPath, shaderText);
        AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null || ShaderUtil.ShaderHasError(shader) || !shader.isSupported)
            throw new System.InvalidOperationException("Water shader did not compile. Check the Console.");
        Material material = new Material(shader);
        material.SetColor("_WaterColor", new Color(0.04f, 0.45f, 0.64f, 1f));
        material.SetColor("_FoamColor", new Color(0.65f, 0.92f, 0.97f, 1f));
        material.SetFloat("_FlowSpeed", flowSpeed);
        material.SetVector("_RiverData", new Vector4(area.center.x, area.min.z, area.size.z, EffectiveBend(area)));
        // Each generation gets a saved material so other scenes keep their settings.
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/RiverWater.mat");
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static string WaterShader(bool urp)
    {
        string header = @"Shader ""LEGO River/Animated Water PIPELINE""
{
    Properties
    {
        _WaterColor(""Water Color"", Color) = (0.04, 0.45, 0.64, 1)
        _FoamColor(""Ripple Color"", Color) = (0.65, 0.92, 0.97, 1)
        _FlowSpeed(""Flow Speed"", Float) = 2
        _RiverData(""River Layout"", Vector) = (0,0,200,20)
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""Queue""=""Geometry"" PIPELINE_TAG }
        Pass
        {
            Tags { ""LightMode""=""LIGHT_MODE"" }
            Cull Off
            ZWrite On
            PROGRAM_BEGIN
            #pragma vertex vert
            #pragma fragment frag
            SHADER_INCLUDE
            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor;
                float4 _FoamColor;
                float4 _RiverData;
                float _FlowSpeed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                VERTEX_TRANSFORM
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float t = (input.positionWS.z - _RiverData.y) / max(0.01, _RiverData.z);
                float centre = _RiverData.x + _RiverData.w *
                    (0.72 * sin(t * 6.2831853) + 0.28 * sin(t * 12.5663706 + 0.7));
                float u = (input.positionWS.x - centre) * 0.6;
                float v = (input.positionWS.z - _Time.y * _FlowSpeed) * 0.6;
                float ripple = 0.5 + 0.5 * sin(v * 2.8 + sin(u * 1.2));
                float streak = sin(v * 1.8 + sin(u * 3.0)) * sin(u * 2.3 + v * 0.2);
                float foam = smoothstep(0.78, 1.0, streak);
                half3 color = lerp(_WaterColor.rgb, _FoamColor.rgb, 0.12 * ripple + 0.55 * foam);
                return half4(color, 1);
            }
            PROGRAM_END
        }
    }
    Fallback Off
}
";
        header = header.Replace("PIPELINE_TAG", urp ? "\"RenderPipeline\"=\"UniversalPipeline\"" : "");
        header = header.Replace("PIPELINE", urp ? "URP" : "BuiltIn");
        header = header.Replace("LIGHT_MODE", urp ? "SRPDefaultUnlit" : "Always");
        header = header.Replace("PROGRAM_BEGIN", urp ? "HLSLPROGRAM" : "CGPROGRAM");
        header = header.Replace("PROGRAM_END", urp ? "ENDHLSL" : "ENDCG");
        header = header.Replace("SHADER_INCLUDE", urp
            ? "#include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\""
            : "#include \"UnityCG.cginc\"");
        return header.Replace("VERTEX_TRANSFORM", urp
            ? "output.positionCS = TransformObjectToHClip(input.positionOS.xyz); output.positionWS = TransformObjectToWorld(input.positionOS.xyz);"
            : "output.positionCS = UnityObjectToClipPos(input.positionOS); output.positionWS = mul(unity_ObjectToWorld, input.positionOS).xyz;");
    }

    private void CreateFoundation(Bounds area, float width, float depth,
        int startColumn, int endColumn, int row, GameObject source)
    {
        // Merge each uninterrupted row into a single base block. This closes
        // the island sides without thousands of additional foundation cubes.
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Sand Plate Foundation " + row + ":" + startColumn;
        block.transform.SetParent(generatedRoot.transform, false);
        block.layer = source.layer;
        block.transform.localScale = new Vector3((endColumn - startColumn) * width,
            area.size.y, depth);
        block.transform.position = new Vector3(
            area.min.x + (startColumn + endColumn) * 0.5f * width,
            area.center.y, area.min.z + (row + 0.5f) * depth);
        Renderer renderer = block.GetComponent<Renderer>();
        Renderer sourceRenderer = source.GetComponent<Renderer>();
        if (sandMaterial != null) renderer.sharedMaterial = sandMaterial;
        else if (sourceRenderer != null) renderer.sharedMaterial = sourceRenderer.sharedMaterial;
    }

    private static bool TryBounds(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }
}
#endif
