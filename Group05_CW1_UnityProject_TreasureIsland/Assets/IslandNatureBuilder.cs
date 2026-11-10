#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Add ONCE to Assets. Tools > Island Nature Builder.
// Builds reusable studded LEGO-style scenery, or scatters optional asset prefabs.
// No roads, bridges, gates, jumping obstacles or character changes are created.
public class IslandNatureBuilder : EditorWindow
{
    private const string RootName = "Island Nature (Generated)";
    [SerializeField] private GameObject island;
    [SerializeField] private int seed = 7;
    [SerializeField] private int treeCount = 80;
    [SerializeField] private int bushCount = 150;
    [SerializeField] private int rockCount = 60;
    [SerializeField] private float treeHeight = 16f;
    [SerializeField] private float treeSpacing = 16f;
    [SerializeField] private float coastInset = 14f;
    [SerializeField] private float riverClearance = 4f;
    [SerializeField] private float openCentre = 24f;
    [SerializeField] private GameObject treePrefab, bushPrefab, rockPrefab;
    [SerializeField] private bool showPrefabs;
    [SerializeField] private bool solidTrunksAndRocks = true;
    private string status = "Select your round island. Run after the ground and waterfall tools.";
    private Bounds area;
    private List<Vector2> outline;
    private Transform tileRoot, waterfallRoot;
    private readonly List<RiverSection> river = new List<RiverSection>();
    private readonly List<Bounds> excluded = new List<Bounds>();
    private readonly List<Vector2> players = new List<Vector2>();
    private readonly List<Vector2> anchors = new List<Vector2>();
    private readonly List<Plant> plan = new List<Plant>();
    private readonly float[] prefabRadii = new float[3];
    private System.Random random;

    private struct RiverSection { public Vector2 a, b; public float halfWidth; }
    private sealed class Plant
    {
        public int kind, variant;
        public Vector3 point;
        public float height, yaw, radius;
    }

    [MenuItem("Tools/Island Nature Builder")]
    public static void Open() { GetWindow<IslandNatureBuilder>("Island Nature"); }

    private void OnEnable()
    {
        if (island == null) island = GameObject.Find("round island") ?? GameObject.Find("Round Island");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("LEGO Island Nature", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Adds tree clusters, palms, bushes and rocks. Green ground remains the walking surface. River banks, waterfall and the island centre keep clear space. Default LEGO-style models are included; no downloaded assets are needed.", MessageType.Info);
        island = (GameObject)EditorGUILayout.ObjectField("Round Island", island, typeof(GameObject), true);
        if (GUILayout.Button("Use Selected Round Island")) island = Selection.activeGameObject;
        seed = EditorGUILayout.IntField("Nature Seed", seed);
        treeCount = EditorGUILayout.IntSlider("Trees", treeCount, 0, 150);
        bushCount = EditorGUILayout.IntSlider("Bushes", bushCount, 0, 350);
        rockCount = EditorGUILayout.IntSlider("Rocks", rockCount, 0, 150);
        treeHeight = EditorGUILayout.Slider("Tree Height", treeHeight, 6f, 24f);
        treeSpacing = EditorGUILayout.Slider("Tree Spacing", treeSpacing, 10f, 24f);
        coastInset = EditorGUILayout.Slider("Beach Clearance", coastInset, 8f, 30f);
        riverClearance = EditorGUILayout.Slider("River Clearance", riverClearance, 2f, 12f);
        openCentre = EditorGUILayout.Slider("Open Centre Radius", openCentre, 10f, 45f);
        solidTrunksAndRocks = EditorGUILayout.Toggle("Solid Trunks / Rocks", solidTrunksAndRocks);
        showPrefabs = EditorGUILayout.Foldout(showPrefabs, "Use your own prefabs (optional)");
        if (showPrefabs)
        {
            treePrefab = (GameObject)EditorGUILayout.ObjectField("Tree Prefab", treePrefab, typeof(GameObject), false);
            bushPrefab = (GameObject)EditorGUILayout.ObjectField("Bush Prefab", bushPrefab, typeof(GameObject), false);
            rockPrefab = (GameObject)EditorGUILayout.ObjectField("Rock Prefab", rockPrefab, typeof(GameObject), false);
            EditorGUILayout.LabelField("Empty fields use built-in generated LEGO-style models. Asset prefabs keep their own materials and existing colliders.", EditorStyles.wordWrappedLabel);
        }
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("GENERATE TREES + BUSHES + ROCKS", GUILayout.Height(40))) Generate();
            if (GUILayout.Button("Remove Generated Nature")) Remove();
        }
        EditorGUILayout.HelpBox(status, MessageType.Info);
        EditorGUILayout.LabelField("Save Ctrl+S. Keep Assets/Island Nature Generated and its .meta files when committing. Regenerate nature if you change the ground level or river route.", EditorStyles.wordWrappedLabel);
    }

    private bool Prepare()
    {
        if (island == null || EditorUtility.IsPersistent(island) || !island.activeInHierarchy)
        { status = "Choose the original round island from Hierarchy."; return false; }
        MeshFilter filter = island.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.name != "Cylinder")
        { status = "Choose your round Cylinder island, not a LEGO plate or the generated waterfall group."; return false; }
        Vector3 scale = island.transform.lossyScale;
        if (Quaternion.Angle(island.transform.rotation, Quaternion.identity) > .01f || scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
        { status = "Round island needs Rotation 0,0,0 and positive scale."; return false; }
        foreach (GameObject prefab in new[] { treePrefab, bushPrefab, rockPrefab })
            if (prefab != null && !EditorUtility.IsPersistent(prefab))
            { status = "Optional prefabs must come from the Project window."; return false; }
        GameObject[] prefabs = { treePrefab, bushPrefab, rockPrefab };
        for (int i = 0; i < prefabs.Length; i++)
        {
            prefabRadii[i] = 0;
            if (prefabs[i] == null) continue;
            Bounds measured;
            if (!AssetBounds(prefabs[i], out measured) || measured.size.y < .00001f)
            { status = "An optional prefab has no readable visible mesh. Clear that field to use the included models."; return false; }
            prefabRadii[i] = new Vector2(measured.extents.x, measured.extents.z).magnitude / measured.size.y;
        }
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null && !pipeline.GetType().Name.Contains("Universal"))
        { status = "Generated models support URP and Built-in projects."; return false; }
        Bounds local = filter.sharedMesh.bounds;
        area = new Bounds(island.transform.TransformPoint(local.center), Vector3.Scale(local.size, scale));
        outline = Hull(filter.sharedMesh, island.transform);
        if (outline.Count < 3 || area.size.x < 80 || area.size.z < 80)
        { status = "Island is too small or its outline could not be read."; return false; }
        Scene scene = island.scene;
        GameObject tiles = FindRoot(scene, "LEGO Sand Tiles (Generated)");
        GameObject waterfall = FindRoot(scene, "Island Waterfall (Generated)");
        tileRoot = tiles != null ? tiles.transform : null;
        waterfallRoot = waterfall != null ? waterfall.transform : null;
        river.Clear(); excluded.Clear(); players.Clear();
        if (waterfallRoot != null)
        {
            Transform stream = waterfallRoot.Find("Flowing River");
            MeshFilter riverMesh = stream != null ? stream.GetComponent<MeshFilter>() : null;
            if (riverMesh != null && riverMesh.sharedMesh != null)
            {
                Vector3[] vertices = riverMesh.sharedMesh.vertices;
                // IslandWaterfallBuilder saves each river strip quad as four
                // vertices: leftA, rightA, leftB, rightB.
                for (int i = 0; i + 3 < vertices.Length; i += 4)
                {
                    Vector3 a = stream.TransformPoint(vertices[i]), b = stream.TransformPoint(vertices[i + 1]);
                    Vector3 c = stream.TransformPoint(vertices[i + 2]), d = stream.TransformPoint(vertices[i + 3]);
                    river.Add(new RiverSection {
                        a = XZ((a + b) * .5f), b = XZ((c + d) * .5f),
                        halfWidth = Mathf.Max(Vector3.Distance(a, b), Vector3.Distance(c, d)) * .5f });
                }
            }
            foreach (string name in new[] { "Grassy Rock Mountain", "Waterfall Splash Pool", "Falling Water" })
            {
                Transform child = waterfallRoot.Find(name);
                Renderer renderer = child != null ? child.GetComponent<Renderer>() : null;
                if (renderer != null) excluded.Add(renderer.bounds);
            }
        }
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (CharacterController character in root.GetComponentsInChildren<CharacterController>())
                if (character.gameObject.activeInHierarchy) players.Add(XZ(character.transform.position));
        Physics.SyncTransforms();
        return true;
    }

    private float Next(float low, float high) { return low + (high - low) * (float)random.NextDouble(); }
    private Vector2 Around(Vector2 centre, float low, float high)
    {
        float angle = Next(0, Mathf.PI * 2f), radius = Mathf.Sqrt(Next(low * low, high * high));
        return centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }
    private Vector2 Uniform() { return new Vector2(Next(area.min.x, area.max.x), Next(area.min.z, area.max.z)); }

    private bool Safe(Vector2 point, float radius)
    {
        if (Shore(point) < coastInset + radius) return false;
        if (Vector2.Distance(point, XZ(area.center)) < openCentre + radius) return false;
        foreach (Vector2 player in players) if (Vector2.Distance(point, player) < 12f + radius) return false;
        foreach (Bounds bounds in excluded)
            if (point.x >= bounds.min.x - radius - 5f && point.x <= bounds.max.x + radius + 5f
                && point.y >= bounds.min.z - radius - 5f && point.y <= bounds.max.z + radius + 5f) return false;
        foreach (RiverSection section in river)
            if (SegmentDistance(point, section.a, section.b) < section.halfWidth + riverClearance + radius) return false;
        return true;
    }

    private bool Ground(Vector2 point, out Vector3 position)
    {
        position = Vector3.zero;
        Vector3 origin = new Vector3(point.x, area.max.y + 120f, point.y);
        float best = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.gameObject.scene != island.scene || hit.distance >= best || hit.normal.y < .88f) continue;
            Transform t = hit.collider.transform;
            bool eligible = t == island.transform
                || (tileRoot != null && t.IsChildOf(tileRoot))
                || (waterfallRoot != null && t.IsChildOf(waterfallRoot) && t.name == "River Channel Ground");
            if (!eligible) continue;
            // Reject terrain sloping down into water even if the river mask
            // came from an older generated layout.
            if (hit.point.y < area.max.y - .15f || hit.point.y > area.max.y + 2f) continue;
            best = hit.distance; position = hit.point;
        }
        return !float.IsPositiveInfinity(best);
    }

    private bool Spaced(Vector2 point, int kind, float radius)
    {
        foreach (Plant existing in plan)
        {
            float distance;
            if (kind == 0 && existing.kind == 0) distance = treeSpacing;
            else if (kind == 0 || existing.kind == 0)
                distance = kind == 2 || existing.kind == 2 ? radius + existing.radius + 1f : 3.8f;
            else if (kind == 2 || existing.kind == 2) distance = radius + existing.radius + 1.5f;
            else distance = 2.7f;
            if (Vector2.Distance(point, XZ(existing.point)) < distance) return false;
        }
        return true;
    }

    private void Plan()
    {
        random = new System.Random(seed);
        anchors.Clear(); plan.Clear();
        for (int i = 0; i < 1000 && anchors.Count < 10; i++)
        {
            Vector2 point = Uniform();
            if (!Safe(point, 12f)) continue;
            bool clear = true;
            foreach (Vector2 anchor in anchors) if (Vector2.Distance(point, anchor) < 45f) { clear = false; break; }
            if (clear) anchors.Add(point);
        }
        int[] counts = { treeCount, bushCount, rockCount };
        var treePositions = new List<Vector2>();
        for (int kind = 0; kind < 3; kind++)
        {
            int placed = 0, attempts = Mathf.Max(100, counts[kind] * 100);
            for (int attempt = 0; attempt < attempts && placed < counts[kind]; attempt++)
            {
                if (attempt % 100 == 0 && EditorUtility.DisplayCancelableProgressBar("Planning island nature",
                    "Finding clear green ground for " + (kind == 0 ? "trees" : kind == 1 ? "bushes" : "rocks"), kind / 3f))
                    throw new OperationCanceledException();
                Vector2 point;
                if (kind == 1 && treePositions.Count > 0 && random.NextDouble() < .8)
                    point = Around(treePositions[random.Next(treePositions.Count)], 4f, 10f);
                else if (anchors.Count > 0 && random.NextDouble() < .75)
                    point = Around(anchors[random.Next(anchors.Count)], 0f, kind == 0 ? 28f : 33f);
                else point = Uniform();
                int variant = kind == 0 ? (random.NextDouble() < .34 ? 2 : random.Next(2)) : random.Next(2);
                float height = kind == 0 ? treeHeight * Next(.82f, 1.18f) * (variant == 2 ? 1.10f : 1f)
                    : kind == 1 ? Next(1.4f, 2.6f) : Next(1.6f, 3.7f);
                float radius = height * Mathf.Max(prefabRadii[kind], kind == 0 ? .55f : kind == 1 ? .95f : .85f);
                Vector3 ground;
                if (!Safe(point, radius) || !Spaced(point, kind, radius) || !Ground(point, out ground)) continue;
                plan.Add(new Plant { kind = kind, variant = variant, point = ground,
                    height = height, radius = radius, yaw = Next(0f, 360f) });
                if (kind == 0) treePositions.Add(point);
                placed++;
            }
        }
    }

    private void Generate()
    {
        if (!Prepare()) return;
        int undo = -1;
        try
        {
            Plan();
            EditorUtility.ClearProgressBar();
            if (plan.Count == 0) { status = "No suitable ground found. Check Round Island and its ground colliders."; return; }
            string folder = NewDirectory();
            Material[] palette = Palette(folder);
            Mesh[] trees = { Save(folder, "Leaf Tree A", LeafTree(false)), Save(folder, "Leaf Tree B", LeafTree(true)), Save(folder, "Palm Tree", Palm()) };
            Mesh[] bushes = { Save(folder, "Bush A", Bush(false)), Save(folder, "Bush B", Bush(true)) };
            Mesh[] rocks = { Save(folder, "Rock A", Rock(false)), Save(folder, "Rock B", Rock(true)) };
            AssetDatabase.SaveAssets();
            Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate Island Nature");
            GameObject old = FindRoot(island.scene, RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            GameObject root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, island.scene);
            Undo.RegisterCreatedObjectUndo(root, "Generate Island Nature");
            Transform treeGroup = Group(root.transform, "LEGO Trees");
            Transform bushGroup = Group(root.transform, "LEGO Bushes");
            Transform rockGroup = Group(root.transform, "LEGO Rocks");
            int[] placed = new int[3];
            foreach (Plant plant in plan)
            {
                GameObject prefab = plant.kind == 0 ? treePrefab : plant.kind == 1 ? bushPrefab : rockPrefab;
                Mesh mesh = plant.kind == 0 ? trees[plant.variant] : plant.kind == 1 ? bushes[plant.variant] : rocks[plant.variant];
                Transform parent = plant.kind == 0 ? treeGroup : plant.kind == 1 ? bushGroup : rockGroup;
                Place(plant, prefab, mesh, palette, parent, ++placed[plant.kind]);
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(island.scene);
            Selection.activeGameObject = root;
            SceneView.RepaintAll();
            status = "Created " + placed[0] + "/" + treeCount + " trees, " + placed[1] + "/" + bushCount
                + " bushes and " + placed[2] + "/" + rockCount + " rocks. Green walking areas kept open. Save Ctrl+S.";
        }
        catch (OperationCanceledException)
        { if (undo >= 0) Undo.RevertAllDownToGroup(undo); status = "Cancelled. Previous scenery kept."; }
        catch (Exception ex)
        {
            if (undo >= 0) Undo.RevertAllDownToGroup(undo);
            status = "Generation stopped: " + ex.Message + ". Scene changes were undone.";
            Debug.LogException(ex);
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    private static Transform Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Create Nature Group"); return go.transform;
    }

    private void Place(Plant plant, GameObject prefab, Mesh mesh, Material[] palette, Transform parent, int number)
    {
        GameObject go;
        if (prefab != null)
        {
            go = PrefabUtility.InstantiatePrefab(prefab, island.scene) as GameObject;
            if (go == null) go = Instantiate(prefab);
            if (go.scene != island.scene) SceneManager.MoveGameObjectToScene(go, island.scene);
            go.transform.SetParent(parent, false);
        }
        else
        {
            go = new GameObject(); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = palette;
        }
        go.name = (plant.kind == 0 ? (plant.variant == 2 && prefab == null ? "Palm " : "Tree ") : plant.kind == 1 ? "Bush " : "Rock ") + number;
        go.SetActive(true);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.Euler(0f, plant.yaw, 0f) * go.transform.rotation;
        Bounds bounds;
        if (!BoundsOf(go, out bounds) || bounds.size.y < .00001f)
        { DestroyImmediate(go); throw new InvalidOperationException("A selected prefab has no usable visible mesh."); }
        go.transform.localScale *= plant.height / bounds.size.y;
        BoundsOf(go, out bounds);
        go.transform.position += plant.point - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true; body.useGravity = false;
            if (PrefabUtility.IsPartOfPrefabInstance(body)) PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        }
        if (solidTrunksAndRocks && prefab == null && plant.kind != 1)
        {
            if (plant.kind == 2) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            else
            {
                // A narrow trunk collider leaves the leafy canopy walkable below.
                CapsuleCollider collider = go.AddComponent<CapsuleCollider>();
                float localHeight = mesh.bounds.size.y;
                collider.height = localHeight * .65f;
                collider.radius = localHeight * .055f;
                collider.center = new Vector3(0f, collider.height * .5f, 0f);
            }
        }
        foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = island.layer;
            if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
        }
        if (PrefabUtility.IsPartOfPrefabInstance(go.transform))
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        Undo.RegisterCreatedObjectUndo(go, "Place Island Nature");
    }

    private void Remove()
    {
        if (island == null) { status = "Select your round island first."; return; }
        GameObject root = FindRoot(island.scene, RootName);
        if (root == null) { status = "No generated nature found in this scene."; return; }
        Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(island.scene);
        status = "Generated nature removed. Ground, waterfall and character were kept. Save Ctrl+S.";
    }

    private static bool BoundsOf(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds(); bool found = false;
        foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static bool AssetBounds(GameObject asset, out Bounds bounds)
    {
        bounds = new Bounds(); bool found = false;
        foreach (MeshFilter filter in asset.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds local = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = filter.transform.TransformPoint(corner);
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
            }
        }
        return found || BoundsOf(asset, out bounds);
    }

    private static GameObject FindRoot(Scene scene, string name)
    { foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) return root; return null; }
    private static Vector2 XZ(Vector3 p) { return new Vector2(p.x, p.z); }
    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, delta) / Mathf.Max(delta.sqrMagnitude, .000001f));
        return Vector2.Distance(p, a + t * delta);
    }
    private float Shore(Vector2 p)
    {
        float distance = float.PositiveInfinity;
        for (int i = 0; i < outline.Count; i++)
        { Vector2 a = outline[i], b = outline[(i + 1) % outline.Count]; distance = Mathf.Min(distance, Cross(a,b,p) / Mathf.Max((b-a).magnitude,.000001f)); }
        return distance;
    }
    private static float Cross(Vector2 a, Vector2 b, Vector2 p) { return (b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x); }
    private static List<Vector2> Hull(Mesh mesh, Transform transform)
    {
        var unique = new HashSet<Vector2>();
        foreach (Vector3 vertex in mesh.vertices) unique.Add(XZ(transform.TransformPoint(vertex)));
        var points = new List<Vector2>(unique);
        points.Sort((a,b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y.CompareTo(b.y));
        var hull = new List<Vector2>();
        foreach (Vector2 p in points)
        { while(hull.Count>=2&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p); }
        int lower=hull.Count;
        for(int i=points.Count-2;i>=0;i--)
        { Vector2 p=points[i];while(hull.Count>lower&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p); }
        if(hull.Count>0)hull.RemoveAt(hull.Count-1);
        return hull;
    }

    private static string NewDirectory()
    {
        const string parent="Assets/Island Nature Generated";
        if(!AssetDatabase.IsValidFolder(parent))AssetDatabase.CreateFolder("Assets","Island Nature Generated");
        string folder=AssetDatabase.GenerateUniqueAssetPath(parent+"/Setup");
        AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));return folder;
    }
    private static Mesh Save(string folder,string name,Mesh mesh)
    { AssetDatabase.CreateAsset(mesh,folder+"/"+name+".asset");return mesh; }
    private static Material[] Palette(string folder)
    {
        bool urp=GraphicsSettings.currentRenderPipeline!=null;
        Shader shader=Shader.Find(urp?"Universal Render Pipeline/Lit":"Standard");
        if(shader==null)throw new InvalidOperationException("A ground-compatible Lit shader could not be found.");
        string[] names={"Trunk Brown","Dark Leaves","Green Leaves","Bright Leaves","Stone Grey","Stone Brown"};
        Color[] colors={new Color(.32f,.16f,.055f),new Color(.07f,.29f,.035f),new Color(.18f,.48f,.065f),new Color(.39f,.68f,.12f),new Color(.47f,.50f,.48f),new Color(.40f,.28f,.17f)};
        var result=new Material[names.Length];
        for(int i=0;i<result.Length;i++)
        {
            Material material=new Material(shader);
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",colors[i]);
            if(material.HasProperty("_Color"))material.SetColor("_Color",colors[i]);
            if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.28f);
            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.28f);
            AssetDatabase.CreateAsset(material,folder+"/"+names[i]+".mat");result[i]=material;
        }
        return result;
    }

    // Built-in fallback models: all parts combine into one reusable mesh per
    // variant, rather than spawning a GameObject for every brick or stud.
    private static Mesh LeafTree(bool second)
    {
        var m=new LegoMesh();
        m.Cylinder(new Vector3(0,3.6f,0),.52f,7.2f,12,0);
        for(int i=1;i<6;i++)m.Cylinder(new Vector3(0,i*1.15f,0),.60f,.18f,12,0);
        m.Box(new Vector3(-.8f,6.1f,0),new Vector3(2.0f,.65f,.7f),0);
        m.Box(new Vector3(.8f,6.9f,.4f),new Vector3(2.2f,.65f,.7f),0);
        if(second)
        {
            m.LeafPlate(new Vector3(-1.8f,7.3f,0),2.4f,.65f,2);
            m.LeafPlate(new Vector3(1.5f,7.8f,-.6f),2.6f,.65f,1);
            m.LeafPlate(new Vector3(0,8.8f,1.1f),2.7f,.65f,2);
            m.LeafPlate(new Vector3(-.5f,9.9f,-.2f),2.2f,.65f,3);
            m.LeafPlate(new Vector3(.2f,10.8f,0),1.3f,.6f,2);
        }
        else
        {
            m.LeafPlate(new Vector3(-1.6f,7.3f,-.2f),2.5f,.65f,1);
            m.LeafPlate(new Vector3(1.5f,7.8f,.3f),2.5f,.65f,2);
            m.LeafPlate(new Vector3(0,8.8f,-.8f),3.0f,.65f,2);
            m.LeafPlate(new Vector3(.1f,9.8f,.3f),2.2f,.65f,3);
            m.LeafPlate(new Vector3(-.3f,10.7f,0),1.3f,.6f,2);
        }
        return m.Finish(second?"LEGO leaf tree B":"LEGO leaf tree A");
    }

    private static Mesh Palm()
    {
        var m=new LegoMesh();
        for(int i=0;i<8;i++)
        {
            float t=i/7f;
            Vector3 centre=new Vector3(t*t*.75f,i*1.25f+.625f,0);
            m.Cylinder(centre,Mathf.Lerp(.65f,.42f,t),1.25f,10,0);
            m.Cylinder(centre+Vector3.up*.52f,Mathf.Lerp(.71f,.49f,t),.18f,10,0);
        }
        Vector3 crown=new Vector3(.75f,10.1f,0);
        for(int i=0;i<7;i++)m.Frond(crown,i*Mathf.PI*2f/7f,5.8f,i%2==0?2:3);
        m.Cylinder(crown+Vector3.up*.18f,.8f,.5f,10,3);
        m.Brick(crown+new Vector3(-.65f,-.5f,.3f),new Vector3(.65f,.6f,.65f),0);
        m.Brick(crown+new Vector3(.35f,-.6f,-.6f),new Vector3(.65f,.6f,.65f),0);
        return m.Finish("LEGO palm tree");
    }

    private static Mesh Bush(bool second)
    {
        var m=new LegoMesh();
        m.Brick(new Vector3(-.7f,.4f,0),new Vector3(1.6f,.8f,1.5f),1);
        m.Brick(new Vector3(.7f,.45f,.2f),new Vector3(1.6f,.9f,1.5f),2);
        m.Brick(new Vector3(second?.35f:-.2f,1.15f,-.1f),new Vector3(1.9f,.65f,1.3f),second?2:3);
        if(second)m.Brick(new Vector3(.2f,1.7f,0),new Vector3(1.0f,.45f,.9f),3);
        return m.Finish(second?"LEGO bush B":"LEGO bush A");
    }

    private static Mesh Rock(bool brown)
    {
        var m=new LegoMesh();int slot=brown?5:4;
        const int sides=7;
        Vector3[][] rings=new Vector3[3][];
        for(int layer=0;layer<3;layer++)
        {
            rings[layer]=new Vector3[sides];
            float y=layer==0?0:layer==1?1.15f:2.6f;
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2f/sides;
                float radius=(layer==0?1.8f:layer==1?2.0f:.8f)*(1+.10f*Mathf.Sin(i*2.7f));
                rings[layer][i]=new Vector3(Mathf.Cos(a)*radius+(layer==2?.25f:0),y,Mathf.Sin(a)*radius);
            }
        }
        for(int layer=0;layer<2;layer++)for(int i=0;i<sides;i++)
        {int j=(i+1)%sides;m.Quad(rings[layer][i],rings[layer+1][i],rings[layer+1][j],rings[layer][j],slot);}
        m.Top(rings[2],slot);
        m.Cylinder(new Vector3(-.1f,2.70f,-.15f),.25f,.2f,10,slot);
        m.Cylinder(new Vector3(.55f,2.70f,.2f),.25f,.2f,10,slot);
        return m.Finish(brown?"LEGO brown rock":"LEGO grey rock");
    }

    private sealed class LegoMesh
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<int>[] triangles=new List<int>[6];
        public LegoMesh(){for(int i=0;i<6;i++)triangles[i]=new List<int>();}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int slot)
        {
            int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
            uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
            Tri(n,n+1,n+2,slot);Tri(n,n+2,n+3,slot);
        }
        private void Tri(int a,int b,int c,int slot)
        {
            if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<1e-12f)return;
            triangles[slot].Add(a);triangles[slot].Add(b);triangles[slot].Add(c);
        }
        public void Top(Vector3[] ring,int slot)
        {
            int n=vertices.Count;foreach(Vector3 v in ring){vertices.Add(v);uv.Add(new Vector2(v.x,v.z));}
            for(int i=1;i<ring.Length-1;i++)Tri(n,n+i+1,n+i,slot);
        }
        public void Box(Vector3 centre,Vector3 size,int slot)
        {
            Vector3 e=size*.5f;
            Vector3 a=centre+new Vector3(-e.x,-e.y,-e.z),b=centre+new Vector3(e.x,-e.y,-e.z);
            Vector3 c=centre+new Vector3(e.x,-e.y,e.z),d=centre+new Vector3(-e.x,-e.y,e.z);
            Vector3 A=a+Vector3.up*size.y,B=b+Vector3.up*size.y,C=c+Vector3.up*size.y,D=d+Vector3.up*size.y;
            Quad(a,b,c,d,slot);Quad(A,D,C,B,slot);
            Quad(a,A,B,b,slot);Quad(b,B,C,c,slot);Quad(c,C,D,d,slot);Quad(d,D,A,a,slot);
        }
        public void Cylinder(Vector3 centre,float radius,float height,int sides,int slot)
        {
            Vector3[] top=new Vector3[sides],bottom=new Vector3[sides];
            for(int i=0;i<sides;i++)
            {float a=i*Mathf.PI*2f/sides;Vector3 r=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);top[i]=centre+r+Vector3.up*height*.5f;bottom[i]=centre+r-Vector3.up*height*.5f;}
            for(int i=0;i<sides;i++){int j=(i+1)%sides;Quad(bottom[i],top[i],top[j],bottom[j],slot);}
            Top(top,slot);Array.Reverse(bottom);Top(bottom,slot);
        }
        public void Brick(Vector3 centre,Vector3 size,int slot)
        {
            Box(centre,size,slot);
            int columns=Mathf.Max(1,Mathf.FloorToInt(size.x/.75f));
            int rows=Mathf.Max(1,Mathf.FloorToInt(size.z/.75f));
            for(int z=0;z<rows;z++)for(int x=0;x<columns;x++)
            {
                Vector3 p=centre+new Vector3(((x+.5f)/columns-.5f)*size.x,size.y*.5f+.07f,((z+.5f)/rows-.5f)*size.z);
                Cylinder(p,.22f,.14f,10,slot);
            }
        }
        public void LeafPlate(Vector3 centre,float radius,float height,int slot)
        {
            Cylinder(centre,radius,height,8,slot);
            for(float z=-radius+.6f;z<=radius-.3f;z+=1.05f)
                for(float x=-radius+.6f;x<=radius-.3f;x+=1.05f)
                    if(x*x+z*z<(radius-.4f)*(radius-.4f))Cylinder(centre+new Vector3(x,height*.5f+.08f,z),.24f,.16f,10,slot);
        }
        public void Frond(Vector3 origin,float angle,float length,int slot)
        {
            Vector3 forward=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            Vector3 across=new Vector3(-forward.z,0,forward.x);
            for(int i=0;i<6;i++)
            {
                float a=i/6f,b=(i+1)/6f;
                Vector3 p=origin+forward*(length*a)+Vector3.up*(1.2f*Mathf.Sin(a*Mathf.PI)-1.1f*a*a);
                Vector3 q=origin+forward*(length*b)+Vector3.up*(1.2f*Mathf.Sin(b*Mathf.PI)-1.1f*b*b);
                float wa=.10f+.85f*Mathf.Sin(a*Mathf.PI),wb=.10f+.85f*Mathf.Sin(b*Mathf.PI);
                Vector3 A=p-across*wa,B=p+across*wa,C=q+across*wb,D=q-across*wb;
                // Double-sided solid plastic leaf with a narrow raised rib.
                Quad(A,B,C,D,slot);Quad(A-Vector3.up*.13f,D-Vector3.up*.13f,C-Vector3.up*.13f,B-Vector3.up*.13f,slot);
                Quad(A,A-Vector3.up*.13f,B-Vector3.up*.13f,B,slot);
                Quad(D,C,C-Vector3.up*.13f,D-Vector3.up*.13f,slot);
                Quad(A,D,D-Vector3.up*.13f,A-Vector3.up*.13f,slot);
                Quad(B,B-Vector3.up*.13f,C-Vector3.up*.13f,C,slot);
            }
        }
        public Mesh Finish(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=6;
            for(int i=0;i<6;i++)mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
#endif
