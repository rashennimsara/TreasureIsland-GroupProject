#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Add this file once to Assets. Open Tools > Island Waterfall Builder.
// Run AFTER generating LEGO ground. Meshes and materials are saved as assets;
// no runtime C# component or external package is required. URP project required.
public class IslandWaterfallBuilder : EditorWindow
{
    private const string RootName = "Island Waterfall (Generated)";
    private const string TileRootName = "LEGO Sand Tiles (Generated)";
    private const string HiddenName = "Sand Plate Waterfall Hidden Ground Tiles";
    [SerializeField] private GameObject island;
    [SerializeField] private GameObject ocean;
    [SerializeField] private Material sand;
    [SerializeField] private Material grass;
    [SerializeField] private float mountainHeight = 24f;
    [SerializeField] private float riverWidth = 10f;
    [SerializeField] private float beachWidth = 8f;
    [SerializeField] private float gridSize = 4f;
    [SerializeField] private float flowSpeed = 1.2f;
    private string status = "Select your round island. Stop Play mode before building.";
    private Bounds area;
    private List<Vector2> coast;
    private readonly List<Vector2> path = new List<Vector2>();
    private readonly List<float> distances = new List<float>();
    private float pathLength, seaY, startY;
    private float effectiveGrid, siteScale;
    private const float BankWidth = 2.5f;

    [MenuItem("Tools/Island Waterfall Builder")]
    public static void Open() { GetWindow<IslandWaterfallBuilder>("Island Waterfall"); }

    private void OnEnable()
    {
        if (island == null) island = GameObject.Find("round island") ?? GameObject.Find("Round Island");
        if (ocean == null) ocean = GameObject.Find("Ocean");
        if (island != null && sand == null && island.GetComponent<Renderer>() != null)
            sand = island.GetComponent<Renderer>().sharedMaterial;
        if (grass == null)
        {
            string[] matches = AssetDatabase.FindAssets("GreenGround t:Material");
            if (matches.Length > 0) grass = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matches[0]));
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Mountain, Waterfall and River", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Designed for the 400 x 400 round island. Run after LEGO Ground Builder. Water runs from a small grassy mountain into the sea. Building replaces the island's base collider and hides only plates along the river. Ctrl+Z undoes a build.", MessageType.Info);
        island = (GameObject)EditorGUILayout.ObjectField("Round Island", island, typeof(GameObject), true);
        ocean = (GameObject)EditorGUILayout.ObjectField("Ocean", ocean, typeof(GameObject), true);
        sand = (Material)EditorGUILayout.ObjectField("Sand Material", sand, typeof(Material), false);
        grass = (Material)EditorGUILayout.ObjectField("Green Ground Material", grass, typeof(Material), false);
        if (GUILayout.Button("Use Selected Round Island"))
        {
            island = Selection.activeGameObject;
            OnEnable();
        }
        mountainHeight = EditorGUILayout.Slider("Mountain Height", mountainHeight, 8f, 45f);
        riverWidth = EditorGUILayout.Slider("River Width", riverWidth, 5f, 18f);
        beachWidth = Mathf.Max(0f, EditorGUILayout.FloatField("Beach Width", beachWidth));
        gridSize = EditorGUILayout.Slider("Ground Detail", gridSize, 2f, 8f);
        flowSpeed = EditorGUILayout.Slider("Water Flow Speed", flowSpeed, 0.2f, 4f);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("BUILD MOUNTAIN + WATERFALL + RIVER", GUILayout.Height(40))) Build();
            if (GUILayout.Button("Remove Waterfall Setup / Restore Ground")) Remove();
        }
        EditorGUILayout.HelpBox(status, MessageType.Info);
        EditorGUILayout.LabelField("Save the scene with Ctrl+S. Commit the scene and Assets/Island Waterfall Generated together. Rebuild this setup if you regenerate LEGO tiles.", EditorStyles.wordWrappedLabel);
    }

    private bool Prepare()
    {
        if (island == null || EditorUtility.IsPersistent(island) || !island.activeInHierarchy)
        { status = "Choose the round island from Hierarchy."; return false; }
        MeshFilter filter = island.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.name != "Cylinder"
            || island.GetComponent<MeshCollider>() == null)
        { status = "Choose the original round Cylinder island with its Mesh Collider."; return false; }
        if (Quaternion.Angle(island.transform.rotation, Quaternion.identity) > 0.01f
            || island.transform.lossyScale.x <= 0 || island.transform.lossyScale.y <= 0 || island.transform.lossyScale.z <= 0)
        { status = "Round island needs Rotation 0,0,0 and positive scale."; return false; }
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null || !pipeline.GetType().Name.Contains("Universal"))
        { status = "This waterfall tool requires the Universal Render Pipeline (URP)."; return false; }
        Bounds local = filter.sharedMesh.bounds;
        area = new Bounds(island.transform.TransformPoint(local.center), Vector3.Scale(local.size, island.transform.lossyScale));
        if (area.size.x < 200 || area.size.z < 200 || area.size.x > 1000 || area.size.z > 1000)
        { status = "Use an island between 200 and 1000 units wide; the defaults suit Scale 400,1,400."; return false; }
        effectiveGrid = Mathf.Min(gridSize, riverWidth * .4f);
        siteScale = Mathf.Min(area.size.x, area.size.z) / 400f;
        if ((long)Mathf.CeilToInt(area.size.x/effectiveGrid) * Mathf.CeilToInt(area.size.z/effectiveGrid) > 80000)
        { status = "Ground is too detailed for this island size. Increase Ground Detail and River Width, then try again."; return false; }
        coast = Footprint(filter.sharedMesh, island.transform);
        if (coast.Count < 3) { status = "Could not read the island outline."; return false; }
        startY = area.max.y - 0.35f;
        seaY = area.max.y - 1f;
        if (ocean != null)
        {
            Renderer renderer = ocean.GetComponent<Renderer>();
            seaY = renderer != null ? renderer.bounds.max.y : ocean.transform.position.y;
        }
        if (seaY > startY)
        { status = "Ocean is too high. Lower its water surface below the island top, then build again."; return false; }
        CreatePath();
        return true;
    }

    private void CreatePath()
    {
        path.Clear(); distances.Clear();
        Vector2 centre = new Vector2(area.center.x, area.center.z);
        Vector2[] controls = {
            new Vector2(-.18f, -.10f), new Vector2(-.14f, -.015f),
            new Vector2(-.055f, .095f), new Vector2(.055f, .20f),
            new Vector2(.02f, .31f), new Vector2(.12f, .415f), new Vector2(.10f, .56f)
        };
        for (int i = 0; i < controls.Length; i++)
            controls[i] = centre + Vector2.Scale(controls[i], new Vector2(area.size.x, area.size.z));
        for (int segment = 0; segment < controls.Length - 1; segment++)
            for (int j = 0; j < 16; j++)
            {
                float t = j / 16f;
                Vector2 a = controls[Mathf.Max(0, segment - 1)], b = controls[segment];
                Vector2 c = controls[segment + 1], d = controls[Mathf.Min(controls.Length - 1, segment + 2)];
                path.Add(0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t
                    + (-a + 3f * b - 3f * c + d) * t * t * t));
            }
        path.Add(controls[controls.Length - 1]);
        pathLength = 0;
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0) pathLength += Vector2.Distance(path[i - 1], path[i]);
            distances.Add(pathLength);
        }
    }

    private float DistanceToRiver(Vector2 point, out float along)
    {
        float best = float.PositiveInfinity; along = 0;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 delta = path[i + 1] - path[i];
            float t = Mathf.Clamp01(Vector2.Dot(point - path[i], delta) / Mathf.Max(delta.sqrMagnitude, 0.000001f));
            float sq = (point - path[i] - delta * t).sqrMagnitude;
            if (sq < best) { best = sq; along = Mathf.Lerp(distances[i], distances[i + 1], t); }
        }
        return Mathf.Sqrt(best);
    }

    private float HalfWidth(float along) { return riverWidth * 0.5f * (1f + 0.5f * Mathf.Exp(-along / 14f)); }
    private float WaterY(float along) { return Mathf.Lerp(startY, seaY, Mathf.SmoothStep(0f, 1f, along / pathLength)); }
    private float GroundY(Vector2 point)
    {
        float along;
        float distance = DistanceToRiver(point, out along);
        float width = HalfWidth(along);
        float bank = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width - 0.2f, width + BankWidth, distance));
        return Mathf.Lerp(WaterY(along) - 0.9f, area.max.y, bank);
    }

    private void Build()
    {
        if (!Prepare()) return;
        int undoGroup = -1;
        try
        {
            // Prepare and save geometry before changing any existing scene objects.
            string directory = NewDirectory();
            Material sandMat = sand != null ? sand : LitMaterial(directory, "Sand", new Color(.73f, .65f, .35f));
            Material grassMat = grass != null ? grass : LitMaterial(directory, "Grass", new Color(.22f, .5f, .12f));
            Material rock = LitMaterial(directory, "Mountain Rock", new Color(.34f, .30f, .24f));
            Material river = WaterMaterial(directory, "River", flowSpeed, 0.25f);
            Material falling = WaterMaterial(directory, "Waterfall", flowSpeed * 2.2f, 0.75f);
            Mesh landMesh = SaveMesh(directory, "Channel Ground", BuildGround());
            Mesh hillMesh = SaveMesh(directory, "Mountain", BuildMountain());
            Mesh riverMesh = SaveMesh(directory, "River", BuildRiver());
            Mesh fallMesh = SaveMesh(directory, "Waterfall", BuildFall());
            Mesh poolMesh = SaveMesh(directory, "Splash Pool", Pool(path[0], WaterY(0) + .045f, riverWidth * .65f));
            Mesh sourceMesh = SaveMesh(directory, "Mountain Spring", Spring());
            AssetDatabase.SaveAssets();

            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Island Waterfall");
            Scene scene = island.scene;
            RestoreTiles(scene);
            GameObject old = FindRoot(scene, RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            GameObject root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Build Island Waterfall");
            MeshObject(root, "River Channel Ground", landMesh, new[] { sandMat, grassMat }, true);
            MeshObject(root, "Grassy Rock Mountain", hillMesh, new[] { rock, grassMat }, true);
            MeshObject(root, "Flowing River", riverMesh, new[] { river }, false);
            MeshObject(root, "Falling Water", fallMesh, new[] { falling }, false);
            MeshObject(root, "Waterfall Splash Pool", poolMesh, new[] { falling }, false);
            MeshObject(root, "Mountain Spring", sourceMesh, new[] { river }, false);
            int hidden = HideRiverTiles(scene);
            SetSourceEnabled(false);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            SceneView.RepaintAll();
            status = "Created mountain, waterfall, pool and river. Hidden " + hidden + " river-path plates. Save Ctrl+S and Play. Generated assets: " + directory;
        }
        catch (Exception ex)
        {
            if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
            status = "Build stopped: " + ex.Message + ". Check Console. Prepared assets may remain, but scene changes were undone.";
            Debug.LogException(ex);
        }
    }

    private void Remove()
    {
        if (island == null) { status = "Choose the original round island first."; return; }
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Restore Island Ground");
        RestoreTiles(island.scene);
        GameObject root = FindRoot(island.scene, RootName);
        if (root != null) Undo.DestroyObjectImmediate(root);
        SetSourceEnabled(true);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(island.scene);
        status = "Waterfall setup removed and original ground restored. Save Ctrl+S. Generated mesh/material assets remain available.";
    }

    private void SetSourceEnabled(bool enabled)
    {
        Renderer renderer = island.GetComponent<Renderer>();
        if (renderer != null) { Undo.RecordObject(renderer, "Set Island Ground"); renderer.enabled = enabled; }
        foreach (Collider collider in island.GetComponents<Collider>())
        { Undo.RecordObject(collider, "Set Island Collider"); collider.enabled = enabled; }
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == name) return go;
        return null;
    }

    private static void RestoreTiles(Scene scene)
    {
        GameObject tiles = FindRoot(scene, TileRootName);
        if (tiles == null) return;
        Transform hidden = tiles.transform.Find(HiddenName);
        if (hidden == null) return;
        var children = new List<Transform>();
        foreach (Transform child in hidden) children.Add(child);
        foreach (Transform child in children) Undo.SetTransformParent(child, tiles.transform, "Restore LEGO Plate");
        Undo.DestroyObjectImmediate(hidden.gameObject);
    }

    private int HideRiverTiles(Scene scene)
    {
        GameObject tiles = FindRoot(scene, TileRootName);
        if (tiles == null) return 0;
        var candidates = new List<Transform>();
        foreach (Transform child in tiles.transform)
            if (child.name.StartsWith("Sand Plate ") && !child.name.StartsWith("Sand Plate Foundation")
                && child.name != "Sand Plate River Water") candidates.Add(child);
        GameObject hidden = new GameObject(HiddenName);
        hidden.transform.SetParent(tiles.transform, false);
        hidden.SetActive(false);
        Undo.RegisterCreatedObjectUndo(hidden, "Hide River Plates");
        int count = 0;
        foreach (Transform tile in candidates)
        {
            Renderer[] renderers = tile.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float along;
            float distance = DistanceToRiver(new Vector2(bounds.center.x, bounds.center.z), out along);
            float margin = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            // Use the maximum river width here. A plate can span several
            // centreline samples, including the wider splash-pool section.
            if (distance > riverWidth * .75f + BankWidth + margin) continue;
            Undo.SetTransformParent(tile, hidden.transform, "Hide River Plate");
            count++;
        }
        return count;
    }

    private GameObject MeshObject(GameObject root, string name, Mesh mesh, Material[] materials, bool solid)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.layer = island.layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        if (solid) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        else { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
        Undo.RegisterCreatedObjectUndo(go, "Build Waterfall Object");
        return go;
    }

    private Mesh BuildGround()
    {
        var data = new MeshData(2);
        int columns = Mathf.CeilToInt(area.size.x / effectiveGrid), rows = Mathf.CeilToInt(area.size.z / effectiveGrid);
        float dx = area.size.x / columns, dz = area.size.z / rows;
        for (int z = 0; z < rows; z++) for (int x = 0; x < columns; x++)
        {
            float left = area.min.x + x * dx, bottom = area.min.z + z * dz;
            List<Vector2> polygon = Clip(new List<Vector2> {
                new Vector2(left,bottom), new Vector2(left+dx,bottom),
                new Vector2(left+dx,bottom+dz), new Vector2(left,bottom+dz) }, coast);
            if (polygon.Count < 3) continue;
            Vector2 centre = Vector2.zero;
            foreach (Vector2 p in polygon) centre += p;
            centre /= polygon.Count;
            int material = ShoreDistance(centre) < beachWidth ? 0 : 1;
            var vertices = new List<Vector3>();
            foreach (Vector2 p in polygon) vertices.Add(new Vector3(p.x, GroundY(p), p.y));
            data.Polygon(vertices, material);
        }
        float floor = Mathf.Min(area.min.y, seaY - 2f);
        for (int i = 0; i < coast.Count; i++)
        {
            Vector2 a = coast[i], b = coast[(i + 1) % coast.Count];
            int steps = Mathf.CeilToInt(Vector2.Distance(a,b) / effectiveGrid);
            for (int j = 0; j < steps; j++)
            {
                Vector2 p = Vector2.Lerp(a,b,j/(float)steps), q = Vector2.Lerp(a,b,(j+1)/(float)steps);
                data.Quad(new Vector3(p.x,GroundY(p),p.y), new Vector3(q.x,GroundY(q),q.y),
                    new Vector3(q.x,floor,q.y), new Vector3(p.x,floor,p.y), 0);
            }
        }
        return data.Finish("Island river channel ground");
    }

    private Mesh BuildMountain()
    {
        var data = new MeshData(2);
        const int sides = 20;
        Vector2 spring = path[0];
        float baseY = area.max.y + .18f;
        Vector3[][] rings = new Vector3[3][];
        for (int layer = 0; layer < 3; layer++)
        {
            rings[layer] = new Vector3[sides];
            float radiusX = (layer == 0 ? 39f : layer == 1 ? 29f : 15f) * siteScale;
            float radiusZ = (layer == 0 ? 42f : layer == 1 ? 31f : 16f) * siteScale;
            float centreZ = spring.y - (layer == 2 ? 18f : 26f) * siteScale;
            float y = baseY + mountainHeight * (layer == 0 ? 0f : layer == 1 ? .58f : 1f);
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                rings[layer][i] = new Vector3(spring.x + Mathf.Cos(angle) * radiusX,
                    y, Mathf.Min(spring.y - 2f * siteScale, centreZ + Mathf.Sin(angle) * radiusZ));
            }
        }
        for (int layer = 0; layer < 2; layer++) for (int i = 0; i < sides; i++)
        {
            int j = (i+1)%sides;
            data.Quad(rings[layer][i], rings[layer+1][i], rings[layer+1][j], rings[layer][j], 0);
        }
        data.Polygon(new List<Vector3>(rings[2]), 1);
        return data.Finish("Grassy mountain with waterfall cliff");
    }

    private Mesh BuildRiver()
    {
        var data = new MeshData(1);
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 leftA, rightA, leftB, rightB;
            RiverSides(i, out leftA, out rightA); RiverSides(i+1, out leftB, out rightB);
            data.WaterQuad(new Vector3(leftA.x,WaterY(distances[i])+.025f,leftA.y),
                new Vector3(rightA.x,WaterY(distances[i])+.025f,rightA.y),
                new Vector3(leftB.x,WaterY(distances[i+1])+.025f,leftB.y),
                new Vector3(rightB.x,WaterY(distances[i+1])+.025f,rightB.y),
                distances[i]/4f, distances[i+1]/4f);
        }
        return data.Finish("Winding river water");
    }

    private void RiverSides(int i, out Vector2 left, out Vector2 right)
    {
        Vector2 tangent = (path[Mathf.Min(path.Count-1,i+1)] - path[Mathf.Max(0,i-1)]).normalized;
        Vector2 normal = new Vector2(-tangent.y,tangent.x);
        float width = HalfWidth(distances[i]) - .4f;
        left = path[i] + normal * width; right = path[i] - normal * width;
    }

    private Mesh BuildFall()
    {
        var data = new MeshData(1);
        float top = area.max.y + .18f + mountainHeight + .04f;
        const int steps = 32;
        for (int i = 0; i < steps; i++)
        {
            float a = i/(float)steps, b = (i+1)/(float)steps;
            Vector3 ca = FallCentre(a,top), cb = FallCentre(b,top);
            float wa = Mathf.Lerp(2.5f,riverWidth*.38f,a), wb = Mathf.Lerp(2.5f,riverWidth*.38f,b);
            data.WaterQuad(ca-Vector3.right*wa, ca+Vector3.right*wa,
                cb-Vector3.right*wb, cb+Vector3.right*wb, a*mountainHeight/3f, b*mountainHeight/3f);
        }
        return data.Finish("Waterfall curtain");
    }

    private Vector3 FallCentre(float t, float top)
    {
        return new Vector3(path[0].x, Mathf.Lerp(top,WaterY(0)+.04f,t),
            path[0].y - 1.8f*siteScale*(1f-t) + .75f*siteScale*Mathf.Sin(t*Mathf.PI));
    }

    private Mesh Pool(Vector2 centre, float y, float radius)
    {
        var data = new MeshData(1);
        var points = new List<Vector3>();
        for (int i = 0; i < 32; i++)
        {
            float angle=i*Mathf.PI*2f/32f;
            points.Add(new Vector3(centre.x+radius*Mathf.Cos(angle),y,centre.y+radius*Mathf.Sin(angle)));
        }
        data.Polygon(points,0);
        return data.Finish("Waterfall splash pool");
    }

    private Mesh Spring()
    {
        var data = new MeshData(1);
        float y = area.max.y + .18f + mountainHeight + .045f;
        Vector2 p = path[0];
        data.WaterQuad(new Vector3(p.x-2.5f,y,p.y-12f*siteScale),new Vector3(p.x+2.5f,y,p.y-12f*siteScale),
            new Vector3(p.x-2.5f,y,p.y-1.8f*siteScale),new Vector3(p.x+2.5f,y,p.y-1.8f*siteScale),0,3.4f*siteScale);
        return data.Finish("Mountain spring stream");
    }

    private float ShoreDistance(Vector2 p)
    {
        float distance=float.PositiveInfinity;
        for(int i=0;i<coast.Count;i++)
        {
            Vector2 a=coast[i],b=coast[(i+1)%coast.Count];
            distance=Mathf.Min(distance,Cross(a,b,p)/Mathf.Max(Vector2.Distance(a,b),.000001f));
        }
        return distance;
    }

    private static float Cross(Vector2 a,Vector2 b,Vector2 c)
    { return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x); }

    private static List<Vector2> Footprint(Mesh mesh,Transform transform)
    {
        var unique=new HashSet<Vector2>();
        foreach(Vector3 v in mesh.vertices) { Vector3 p=transform.TransformPoint(v);unique.Add(new Vector2(p.x,p.z)); }
        var points=new List<Vector2>(unique);
        points.Sort((a,b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y.CompareTo(b.y));
        var hull=new List<Vector2>();
        foreach(Vector2 p in points)
        { while(hull.Count>=2&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p); }
        int lower=hull.Count;
        for(int i=points.Count-2;i>=0;i--)
        { Vector2 p=points[i];while(hull.Count>lower&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p); }
        if(hull.Count>0)hull.RemoveAt(hull.Count-1);
        return hull;
    }

    private static List<Vector2> Clip(List<Vector2> polygon,List<Vector2> outline)
    {
        for(int edge=0;edge<outline.Count&&polygon.Count>0;edge++)
        {
            Vector2 a=outline[edge],b=outline[(edge+1)%outline.Count];
            var output=new List<Vector2>();
            Vector2 previous=polygon[polygon.Count-1];float pd=Cross(a,b,previous);
            foreach(Vector2 current in polygon)
            {
                float cd=Cross(a,b,current);
                bool pin=pd>=0,cin=cd>=0;
                if(pin!=cin) output.Add(Vector2.LerpUnclamped(previous,current,pd/(pd-cd)));
                if(cin)output.Add(current);
                previous=current;pd=cd;
            }
            polygon=output;
        }
        return polygon;
    }

    private static string NewDirectory()
    {
        const string parent="Assets/Island Waterfall Generated";
        if(!AssetDatabase.IsValidFolder(parent))AssetDatabase.CreateFolder("Assets","Island Waterfall Generated");
        string path=AssetDatabase.GenerateUniqueAssetPath(parent+"/Setup");
        AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        return path;
    }

    private static Mesh SaveMesh(string folder,string name,Mesh mesh)
    { AssetDatabase.CreateAsset(mesh,folder+"/"+name+".asset");return mesh; }

    private static Material LitMaterial(string folder,string name,Color color)
    {
        Shader shader=Shader.Find("Universal Render Pipeline/Lit");
        if(shader==null)throw new InvalidOperationException("URP Lit shader not found.");
        var material=new Material(shader);material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.12f);
        AssetDatabase.CreateAsset(material,folder+"/"+name+".mat");return material;
    }

    private static Material WaterMaterial(string folder,string name,float speed,float foam)
    {
        const string shaderPath="Assets/Island Waterfall Generated/FlowingWater.shader";
        string source=WaterShader;
        if(!File.Exists(shaderPath)||File.ReadAllText(shaderPath)!=source)File.WriteAllText(shaderPath,source);
        AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceSynchronousImport);
        Shader shader=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if(shader==null||ShaderUtil.ShaderHasError(shader)||!shader.isSupported)
            throw new InvalidOperationException("Flowing water shader could not compile. Check Console.");
        var material=new Material(shader);material.SetFloat("_Speed",speed);material.SetFloat("_Foam",foam);
        AssetDatabase.CreateAsset(material,folder+"/"+name+".mat");return material;
    }

    private sealed class MeshData
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<int>[] triangles;
        public MeshData(int slots){triangles=new List<int>[slots];for(int i=0;i<slots;i++)triangles[i]=new List<int>();}
        public void Polygon(List<Vector3> points,int slot)
        {
            if(points.Count<3)return;
            int start=vertices.Count;
            foreach(Vector3 p in points){vertices.Add(p);uv.Add(new Vector2(p.x/4f,p.z/4f));}
            for(int i=1;i<points.Count-1;i++)Triangle(start,start+i+1,start+i,slot);
        }
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int slot)
        {
            int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
            uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});
            Triangle(n,n+1,n+2,slot);Triangle(n,n+2,n+3,slot);
        }
        public void WaterQuad(Vector3 leftA,Vector3 rightA,Vector3 leftB,Vector3 rightB,float vA,float vB)
        {
            int n=vertices.Count;vertices.AddRange(new[]{leftA,rightA,leftB,rightB});
            uv.AddRange(new[]{new Vector2(0,vA),new Vector2(1,vA),new Vector2(0,vB),new Vector2(1,vB)});
            Triangle(n,n+2,n+1,0);Triangle(n+1,n+2,n+3,0);
        }
        private void Triangle(int a,int b,int c,int slot)
        {
            if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<1e-12f)return;
            triangles[slot].Add(a);triangles[slot].Add(b);triangles[slot].Add(c);
        }
        public Mesh Finish(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=triangles.Length;
            for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }

    private const string WaterShader=@"Shader ""Island/Waterfall Flow URP""
{
 Properties {
  _Speed(""Flow Speed"",Range(0,10))=1.2
  _Foam(""Foam"",Range(0,1))=0.3
  _WaterColor(""Water Color"",Color)=(0.04,0.48,0.62,1)
 }
 SubShader {
  Tags { ""RenderPipeline""=""UniversalPipeline"" ""RenderType""=""Opaque"" }
  Pass {
   Tags { ""LightMode""=""UniversalForwardOnly"" }
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
   CBUFFER_START(UnityPerMaterial)
    float _Speed;float _Foam;float4 _WaterColor;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
   V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.uv=i.uv;return o;}
   half4 frag(V i):SV_Target {
    float v=i.uv.y-_Time.y*_Speed;
    float bands=sin(v*7.0+sin(i.uv.x*22.0));
    float veins=sin(i.uv.x*38.0+sin(v*3.0))*sin(v*11.0+i.uv.x*8.0);
    float foam=smoothstep(0.35,0.95,veins);
    float3 color=lerp(_WaterColor.rgb,float3(0.88,0.98,1.0),saturate(0.10+0.08*bands+foam*_Foam));
    return half4(color,1);
   }
   ENDHLSL
  }
 }
 Fallback Off
}";
}
#endif
