#if UNITY_EDITOR
using System.Collections.Generic;
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
    [SerializeField] private GameObject[] surfaces = new GameObject[0];
    [SerializeField] private float tileSize = 4f;
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
            foreach (string name in new[] { "Island_Centre", "Island_Left", "Island_Right" })
            {
                GameObject go = GameObject.Find(name);
                if (go != null && go.GetComponent<BoxCollider>() != null)
                    found.Add(go);
            }
            surfaces = found.ToArray();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("LEGO Sand Ground", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Stop Play mode. Use a flat, square plate model with studs.", MessageType.Info);
        tileModel = (GameObject)EditorGUILayout.ObjectField("LEGO Plate", tileModel, typeof(GameObject), false);
        sandMaterial = (Material)EditorGUILayout.ObjectField("Sand Material", sandMaterial, typeof(Material), false);
        tileSize = Mathf.Clamp(EditorGUILayout.FloatField("Tile Size", tileSize), 1f, 20f);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sand surfaces: " + (surfaces == null ? 0 : surfaces.Length));
        if (surfaces != null)
            foreach (GameObject surface in surfaces)
                EditorGUILayout.ObjectField(surface, typeof(GameObject), true);
        if (GUILayout.Button("Use Selected Sand Objects"))
        {
            var selected = new List<GameObject>();
            foreach (GameObject go in Selection.gameObjects)
                if (go.GetComponent<BoxCollider>() != null && !EditorUtility.IsPersistent(go))
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
            status = "Select your sand cubes in Hierarchy, then click Use Selected Sand Objects.";
            return;
        }

        var areas = new List<Bounds>();
        var unique = new HashSet<GameObject>();
        Scene targetScene = default(Scene);
        int total = 0;
        float size = Mathf.Clamp(tileSize, 1f, 20f);
        foreach (GameObject surface in surfaces)
        {
            if (surface == null || !unique.Add(surface)) continue;
            BoxCollider box = surface.GetComponent<BoxCollider>();
            if (box == null || !surface.activeInHierarchy || !box.enabled || box.isTrigger) continue;
            if (Quaternion.Angle(surface.transform.rotation, Quaternion.identity) > 0.1f)
            {
                status = "Sand cubes must have world Rotation 0,0,0. Nothing was changed.";
                return;
            }
            if (!targetScene.IsValid()) targetScene = surface.scene;
            if (surface.scene != targetScene)
            {
                status = "Choose sand surfaces from the same scene.";
                return;
            }
            Bounds b = box.bounds;
            if (b.size.x < 0.01f || b.size.z < 0.01f) continue;
            areas.Add(b);
            total += Mathf.CeilToInt(b.size.x / size) * Mathf.CeilToInt(b.size.z / size);
        }
        if (areas.Count == 0 || total > 2500)
        {
            status = areas.Count == 0 ? "No valid sand cubes found." : "Too many tiles. Increase Tile Size (try 4 or 6).";
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

        // Recover our named group after the tool window has been closed and reopened.
        // Only recognize a group containing this generator's named tile children.
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

        foreach (Bounds area in areas)
        {
            int columns = Mathf.CeilToInt(area.size.x / size);
            int rows = Mathf.CeilToInt(area.size.z / size);
            float width = area.size.x / columns;
            float depth = area.size.z / rows;
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    GameObject tile = Instantiate(tileModel, generatedRoot.transform);
                    tile.name = "Sand Plate " + x + "," + z;
                    tile.SetActive(true);
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
                    if (sandMaterial != null)
                        foreach (Renderer renderer in tile.GetComponentsInChildren<Renderer>(true))
                        {
                            Material[] materials = renderer.sharedMaterials;
                            for (int i = 0; i < materials.Length; i++) materials[i] = sandMaterial;
                            renderer.sharedMaterials = materials;
                        }
                }
            }
        }
        Undo.RegisterCreatedObjectUndo(generatedRoot, "Generate LEGO Sand Ground");
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(targetScene);
        status = "Created " + total + " tiles. Save the scene with Ctrl+S. Ctrl+Z undoes generation.";
        SceneView.RepaintAll();
    }

    private void RemoveGenerated()
    {
        if (generatedRoot == null) { status = "No generated ground from this window to remove."; return; }
        Scene scene = generatedRoot.scene;
        Undo.DestroyObjectImmediate(generatedRoot);
        generatedRoot = null;
        EditorSceneManager.MarkSceneDirty(scene);
        status = "Generated tiles removed. Original sand cubes are unchanged.";
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
