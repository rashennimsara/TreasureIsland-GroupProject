#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Add ONCE to Assets. Tools > Island Trail Builder.
// Two winding land trails, separated by an EMPTY river crossing. No bridge,
// stepping stones, obstacles or gameplay scripts are generated.
public class IslandTrailBuilder : EditorWindow
{
    private const string RootName = "Island Trail (Generated)";
    private const string HiddenName = "Trail Hidden Nature";
    [SerializeField] private GameObject island;
    [SerializeField] private GameObject waterfall;
    [SerializeField] private GameObject nature;
    [SerializeField] private float pathWidth = 7f;
    [SerializeField] private float crossingPosition = .55f;
    [SerializeField] private float finishRadius = 8f;
    [SerializeField] private bool clearNature = true;
    [SerializeField] private float natureClearance = 1f;
    [SerializeField] private bool showMarkers = true;
    private string status = "Build after ground, waterfall and nature. Select your round island.";
    private Bounds area, mountainBounds;
    private List<Vector2> coast;
    private readonly List<Vector2> river = new List<Vector2>();
    private readonly List<float> riverWidths = new List<float>();
    private readonly List<float> riverHeights = new List<float>();
    private readonly List<float> riverLengths = new List<float>();
    private readonly List<Vector2> first = new List<Vector2>(), second = new List<Vector2>();
    private readonly List<float> firstLengths = new List<float>(), secondLengths = new List<float>();
    private Vector2 crossing, bankA, bankB, endpoint;
    private float crossingY, rotation, roadLevel;
    private Transform tileRoot;

    [MenuItem("Tools/Island Trail Builder")]
    public static void Open() { GetWindow<IslandTrailBuilder>("Island Trail"); }

    private void OnEnable()
    {
        if (island == null) island = GameObject.Find("round island") ?? GameObject.Find("Round Island");
        if (waterfall == null) waterfall = GameObject.Find("Island Waterfall (Generated)");
        if (nature == null) nature = GameObject.Find("Island Nature (Generated)");
        SceneView.duringSceneGui -= DrawMarkers;
        SceneView.duringSceneGui += DrawMarkers;
    }
    private void OnDisable() { SceneView.duringSceneGui -= DrawMarkers; }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Winding Forest Trail", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("START at the beach, winding through the first forest, then an empty river gap for your teammate's stones. The trail resumes on the other bank, winds through the forest and ends BEHIND the mountain. No shortcut branches or physical river crossing are created.", MessageType.Info);
        island = (GameObject)EditorGUILayout.ObjectField("Round Island", island, typeof(GameObject), true);
        waterfall = (GameObject)EditorGUILayout.ObjectField("Waterfall Group", waterfall, typeof(GameObject), true);
        nature = (GameObject)EditorGUILayout.ObjectField("Nature Group", nature, typeof(GameObject), true);
        if (GUILayout.Button("Use Selected Round Island")) { island = Selection.activeGameObject; OnEnable(); }
        pathWidth = EditorGUILayout.Slider("Path Width", pathWidth, 5f, 12f);
        crossingPosition = EditorGUILayout.Slider("Crossing Position", crossingPosition, .45f, .60f);
        finishRadius = EditorGUILayout.Slider("Finish Clearing Radius", finishRadius, 7f, 14f);
        clearNature = EditorGUILayout.Toggle("Clear Nature From Trail", clearNature);
        natureClearance = EditorGUILayout.Slider("Nature Clearance", natureClearance, .5f, 3f);
        showMarkers = EditorGUILayout.Toggle("Scene View Labels", showMarkers);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("BUILD WINDING TRAIL", GUILayout.Height(40))) Build();
            if (GUILayout.Button("Remove Trail / Restore Hidden Nature")) Remove();
        }
        EditorGUILayout.HelpBox(status, MessageType.Info);
        EditorGUILayout.LabelField("Save Ctrl+S. Start Point, Crossing Bank A/B and End Point are empty marker objects. Run this tool again after regenerating nature or changing the river. Commit Assets/Island Trail Generated with the scene.", EditorStyles.wordWrappedLabel);
    }

    private bool Prepare()
    {
        if (island == null || EditorUtility.IsPersistent(island) || !island.activeInHierarchy)
        { status = "Choose the original round island from Hierarchy."; return false; }
        MeshFilter islandMesh = island.GetComponent<MeshFilter>();
        if (islandMesh == null || islandMesh.sharedMesh == null || islandMesh.sharedMesh.name != "Cylinder")
        { status = "Round Island must be the original Cylinder, not a LEGO tile or generated group."; return false; }
        if (Quaternion.Angle(island.transform.rotation, Quaternion.identity) > .01f)
        { status = "Round island needs world Rotation 0,0,0."; return false; }
        Vector3 scale = island.transform.lossyScale;
        if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
        { status = "Use positive island scales."; return false; }
        Bounds local = islandMesh.sharedMesh.bounds;
        area = new Bounds(island.transform.TransformPoint(local.center), Vector3.Scale(local.size, scale));
        if (area.size.x < 250f || area.size.z < 250f || area.size.x > 1000f || area.size.z > 1000f)
        { status = "This layout suits the 400 x 400 island. Supported size: 250 to 1000 units."; return false; }
        if (waterfall == null || EditorUtility.IsPersistent(waterfall) || waterfall.scene != island.scene || !waterfall.activeInHierarchy)
        { status = "Choose your active Island Waterfall (Generated) group in the same scene."; return false; }
        if (nature != null && (EditorUtility.IsPersistent(nature) || nature.scene != island.scene))
        { status = "Nature Group must be a scene object in the island scene."; return false; }
        Transform stream = waterfall.transform.Find("Flowing River");
        Transform hill = waterfall.transform.Find("Grassy Rock Mountain");
        MeshFilter streamMesh = stream != null ? stream.GetComponent<MeshFilter>() : null;
        Renderer hillRenderer = hill != null ? hill.GetComponent<Renderer>() : null;
        if (streamMesh == null || streamMesh.sharedMesh == null || hillRenderer == null)
        { status = "The waterfall group needs Flowing River and Grassy Rock Mountain from IslandWaterfallBuilder."; return false; }
        coast = Hull(islandMesh.sharedMesh, island.transform);
        if (coast.Count < 3) { status = "Could not read the island outline."; return false; }
        mountainBounds = hillRenderer.bounds;
        GameObject tiles = FindRoot(island.scene, "LEGO Sand Tiles (Generated)");
        tileRoot = tiles != null ? tiles.transform : null;
        roadLevel = area.max.y;
        if(tileRoot!=null)
            foreach(Collider collider in tileRoot.GetComponentsInChildren<Collider>())
                if(collider.enabled&&!collider.isTrigger&&collider.bounds.max.y<=area.max.y+2f)
                    roadLevel=Mathf.Max(roadLevel,collider.bounds.max.y);
        Vector3[] vertices = streamMesh.sharedMesh.vertices;
        river.Clear(); riverWidths.Clear(); riverHeights.Clear(); riverLengths.Clear();
        for (int i = 0; i + 3 < vertices.Length; i += 4)
        {
            Vector3 a = stream.TransformPoint(vertices[i]), b = stream.TransformPoint(vertices[i+1]);
            Vector3 c = stream.TransformPoint(vertices[i+2]), d = stream.TransformPoint(vertices[i+3]);
            if (river.Count == 0) { river.Add(XZ((a+b)*.5f)); riverWidths.Add(Vector3.Distance(a,b)*.5f); riverHeights.Add((a.y+b.y)*.5f); }
            river.Add(XZ((c+d)*.5f)); riverWidths.Add(Vector3.Distance(c,d)*.5f);
            riverHeights.Add((c.y+d.y)*.5f);
        }
        if (river.Count < 3) { status = "Could not read the generated river centreline."; return false; }
        Lengths(river, riverLengths);
        float along = riverLengths[riverLengths.Count-1] * crossingPosition;
        int segment = 0;
        while (segment < river.Count-2 && riverLengths[segment+1] < along) segment++;
        float t = Mathf.InverseLerp(riverLengths[segment], riverLengths[segment+1], along);
        crossing = Vector2.Lerp(river[segment], river[segment+1], t);
        Vector2 tangent = (river[segment+1]-river[segment]).normalized;
        Vector2 left = new Vector2(-tangent.y, tangent.x);
        float waterHalf = Mathf.Lerp(riverWidths[segment],riverWidths[segment+1],t);
        float offset = waterHalf + pathWidth*.5f + 4.5f;
        bankA = crossing + left*offset; bankB = crossing - left*offset;
        // Match the waterfall builder's authored layout, allowing its whole
        // generated group to have been translated or rotated in the scene.
        Vector2 actualDirection = river[river.Count-1]-river[0];
        Vector2 modelDirection = new Vector2(.28f*area.size.x,.66f*area.size.z);
        rotation = Mathf.Atan2(actualDirection.y,actualDirection.x)-Mathf.Atan2(modelDirection.y,modelDirection.x);
        Vector2 backward = Rotate(new Vector2(0,-1));
        float hillExtent = Mathf.Abs(backward.x)*mountainBounds.extents.x + Mathf.Abs(backward.y)*mountainBounds.extents.z;
        endpoint = XZ(mountainBounds.center) + backward*(hillExtent+finishRadius+8f);
        endpoint = KeepInside(endpoint,finishRadius+1f);
        crossingY = (stream.TransformPoint(vertices[segment*4]).y + stream.TransformPoint(vertices[segment*4+2]).y)*.5f;
        MakeRoutes();
        Physics.SyncTransforms();
        return ValidateRoute(first,false) && ValidateRoute(second,true);
    }

    private Vector2 Rotate(Vector2 p)
    {
        float c=Mathf.Cos(rotation),s=Mathf.Sin(rotation);
        return new Vector2(c*p.x-s*p.y,s*p.x+c*p.y);
    }
    private Vector2 Model(float x,float z)
    {
        Vector2 origin=new Vector2(-.18f*area.size.x,-.10f*area.size.z);
        return river[0]+Rotate(new Vector2(x*area.size.x,z*area.size.z)-origin);
    }

    private void MakeRoutes()
    {
        var a=new List<Vector2> {
            Model(-.42f,.25f),Model(-.335f,.295f),Model(-.255f,.235f),Model(-.31f,.145f),
            Model(-.37f,.045f),Model(-.32f,-.035f),Model(-.22f,.03f),Model(-.12f,.07f),
            Model(-.155f,.155f),Model(-.085f,.225f),bankA };
        var b=new List<Vector2> {
            bankB,Model(.185f,.275f),Model(.28f,.205f),Model(.205f,.105f),Model(.305f,.0075f),
            Model(.235f,-.075f),Model(.13f,-.0375f),Model(.055f,-.13f),Model(.155f,-.225f),
            Model(.045f,-.325f),Model(-.06f,-.355f),endpoint };
        Curve(a,first);Curve(b,second);
        for(int i=0;i<first.Count;i++)first[i]=KeepInside(first[i],pathWidth*.5f+.7f);
        for(int i=0;i<second.Count;i++)second[i]=KeepInside(second[i],pathWidth*.5f+.7f);
        Lengths(first,firstLengths);Lengths(second,secondLengths);
    }

    private static void Curve(List<Vector2> nodes,List<Vector2> output)
    {
        output.Clear();var raw=new List<Vector2>();
        for(int i=0;i<nodes.Count-1;i++)for(int j=0;j<20;j++)
        {
            float t=j/20f;Vector2 a=nodes[Mathf.Max(0,i-1)],b=nodes[i],c=nodes[i+1],d=nodes[Mathf.Min(nodes.Count-1,i+2)];
            raw.Add(.5f*(2*b+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));
        }
        raw.Add(nodes[nodes.Count-1]);
        for(int i=0;i<raw.Count-1;i++)
        {
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(raw[i],raw[i+1])/1.5f));
            for(int j=0;j<steps;j++)output.Add(Vector2.Lerp(raw[i],raw[i+1],j/(float)steps));
        }
        output.Add(raw[raw.Count-1]);
    }

    private bool ValidateRoute(List<Vector2> route,bool final)
    {
        float width=pathWidth*.5f;
        foreach(Vector2 p in route)
        {
            if(Shore(p)<width+.35f)
            {status="The trail is too close to the beach edge. Reduce Path Width.";return false;}
            if(InMountain(p,width+.5f))
            {status="The current mountain position overlaps this route. Restore the authored waterfall layout or reduce Path Width.";return false;}
            float riverGap=RiverGap(p);
            if(riverGap<width+2.9f)
            {status="The route is too close to the river. Try Crossing Position 0.55 and Path Width 7.";return false;}
        }
        for(int i=0;i<route.Count-1;i++)for(int j=i+2;j<route.Count-1;j++)
            if(Intersects(route[i],route[i+1],route[j],route[j+1]))
            {status="The adjusted trail would cross itself. Use the default island/waterfall layout and settings.";return false;}
        if(final && InMountain(route[route.Count-1],finishRadius))
        {status="The finish clearing overlaps the mountain. Reduce Finish Clearing Radius.";return false;}
        return true;
    }

    private bool InMountain(Vector2 p,float padding)
    {return p.x>mountainBounds.min.x-padding&&p.x<mountainBounds.max.x+padding&&p.y>mountainBounds.min.z-padding&&p.y<mountainBounds.max.z+padding;}
    private float RiverGap(Vector2 p)
    {
        float distance=float.PositiveInfinity;
        for(int i=0;i<river.Count-1;i++)
            distance=Mathf.Min(distance,SegmentDistance(p,river[i],river[i+1])-Mathf.Max(riverWidths[i],riverWidths[i+1]));
        return distance;
    }

    private float WaterHeight(Vector2 p)
    {
        float best=float.PositiveInfinity,height=riverHeights[0];
        for(int i=0;i<river.Count-1;i++)
        {
            Vector2 delta=river[i+1]-river[i];
            float t=Mathf.Clamp01(Vector2.Dot(p-river[i],delta)/Mathf.Max(delta.sqrMagnitude,.000001f));
            float sq=(p-river[i]-delta*t).sqrMagnitude;
            if(sq<best){best=sq;height=Mathf.Lerp(riverHeights[i],riverHeights[i+1],t);}
        }
        return height;
    }

    private float GroundHeight(Vector2 p)
    {
        float closest=float.PositiveInfinity,height=0;
        Vector3 origin=new Vector3(p.x,area.max.y+100f,p.y);
        foreach(RaycastHit hit in Physics.RaycastAll(origin,Vector3.down,240f,~0,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider.gameObject.scene!=island.scene||hit.distance>=closest||hit.normal.y<.75f)continue;
            Transform t=hit.collider.transform;
            bool ground=t==island.transform||(tileRoot!=null&&t.IsChildOf(tileRoot))
                ||(t.IsChildOf(waterfall.transform)&&t.name=="River Channel Ground");
            if(!ground)continue;
            if(hit.point.y<Mathf.Min(area.max.y-.12f,WaterHeight(p)+.12f)||hit.point.y>area.max.y+2f)continue;
            closest=hit.distance;height=hit.point.y;
        }
        if(float.IsPositiveInfinity(closest))
            throw new InvalidOperationException("No dry ground collider under trail at "+p+". Check the generated ground and colliders");
        return height;
    }

    private float SurfaceY(Vector2 p)
    {
        float ground=GroundHeight(p);
        // Follow the dry river bank down gently instead of ending the paved
        // trail on a raised ledge beside the crossing.
        float blend=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(4f,9f,RiverGap(p)));
        return Mathf.Lerp(ground,Mathf.Max(roadLevel,ground),blend)+.065f;
    }

    private void Build()
    {
        if(!Prepare())return;
        int undo=-1;
        try
        {
            // Finish geometry and assets before mutating existing scene objects.
            string folder=NewDirectory();
            Material material=TrailMaterial(folder);
            Mesh a=Save(folder,"Trail Bank A",Road(first,firstLengths,false));
            Mesh b=Save(folder,"Trail Bank B",Road(second,secondLengths,true));
            AssetDatabase.SaveAssets();
            Undo.IncrementCurrentGroup();undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Build Winding Island Trail");
            RestoreNature();
            GameObject old=FindRoot(island.scene,RootName);
            if(old!=null)Undo.DestroyObjectImmediate(old);
            GameObject root=new GameObject(RootName);SceneManager.MoveGameObjectToScene(root,island.scene);
            Undo.RegisterCreatedObjectUndo(root,"Build Island Trail");
            MeshObject(root,"Trail - Bank A",a,material);
            MeshObject(root,"Trail - Bank B",b,material);
            Marker(root,"Start Point",first[0]);Marker(root,"Crossing Bank A",bankA);
            Marker(root,"Crossing Bank B",bankB);Marker(root,"End Point",endpoint);
            GameObject centre=new GameObject("Crossing Centre - Empty Water");centre.transform.SetParent(root.transform,false);
            centre.transform.position=new Vector3(crossing.x,crossingY+.05f,crossing.y);
            Undo.RegisterCreatedObjectUndo(centre,"Create Crossing Marker");
            int hidden=clearNature?HideNature():0;
            Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(island.scene);
            Selection.activeGameObject=root;SceneView.RepaintAll();
            status="Built two winding land trails. River crossing left empty; finish is behind the mountain. Hidden "+hidden+" nature objects on the trail. Save Ctrl+S.";
        }
        catch(Exception ex)
        {
            if(undo>=0)Undo.RevertAllDownToGroup(undo);
            status="Build stopped: "+ex.Message+". Scene changes were undone. Prepared assets may remain.";
            Debug.LogException(ex);
        }
    }

    private Mesh Road(List<Vector2> route,List<float> lengths,bool final)
    {
        var data=new RoadMesh();const int across=4;
        int[,] rows=new int[route.Count,across+1];
        for(int i=0;i<route.Count;i++)
        {
            Vector2 tangent=(route[Mathf.Min(route.Count-1,i+1)]-route[Mathf.Max(0,i-1)]).normalized;
            Vector2 normal=new Vector2(-tangent.y,tangent.x);
            for(int j=0;j<=across;j++)
            {
                Vector2 p=route[i]+normal*((j/(float)across-.5f)*pathWidth);
                rows[i,j]=data.Vertex(new Vector3(p.x,SurfaceY(p),p.y),new Vector2(j/(float)across*pathWidth/2f,lengths[i]/2f));
            }
        }
        for(int i=0;i<route.Count-1;i++)for(int j=0;j<across;j++)
        {
            data.Triangle(rows[i,j],rows[i,j+1],rows[i+1,j]);
            data.Triangle(rows[i,j+1],rows[i+1,j+1],rows[i+1,j]);
        }
        for(int i=0;i<route.Count-1;i++)
        {
            Vector3 a=data.Position(rows[i,0]),b=data.Position(rows[i+1,0]);
            Vector3 c=data.Position(rows[i,across]),d=data.Position(rows[i+1,across]);
            Vector3 lowA=new Vector3(a.x,GroundHeight(XZ(a))-.02f,a.z),lowB=new Vector3(b.x,GroundHeight(XZ(b))-.02f,b.z);
            Vector3 lowC=new Vector3(c.x,GroundHeight(XZ(c))-.02f,c.z),lowD=new Vector3(d.x,GroundHeight(XZ(d))-.02f,d.z);
            data.Quad(a,b,lowB,lowA);data.Quad(c,lowC,lowD,d);
        }
        AddCircle(data,route[0],pathWidth*.5f);
        AddCircle(data,route[route.Count-1],final?finishRadius:pathWidth*.5f);
        return data.Finish(final?"Winding trail after river":"Winding trail before river");
    }

    private void AddCircle(RoadMesh data,Vector2 centre,float radius)
    {
        int c=data.Vertex(new Vector3(centre.x,SurfaceY(centre)+.005f,centre.y),centre/2f);
        int[] ring=new int[32];
        for(int i=0;i<ring.Length;i++)
        {
            float a=i*Mathf.PI*2f/ring.Length;Vector2 p=centre+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
            ring[i]=data.Vertex(new Vector3(p.x,SurfaceY(p)+.005f,p.y),p/2f);
        }
        for(int i=0;i<ring.Length;i++)data.Triangle(c,ring[(i+1)%ring.Length],ring[i]);
        for(int i=0;i<ring.Length;i++)
        {
            Vector3 a=data.Position(ring[i]),b=data.Position(ring[(i+1)%ring.Length]);
            data.Quad(a,b,new Vector3(b.x,GroundHeight(XZ(b))-.02f,b.z),new Vector3(a.x,GroundHeight(XZ(a))-.02f,a.z));
        }
    }

    private void MeshObject(GameObject root,string name,Mesh mesh,Material material)
    {
        GameObject go=new GameObject(name);go.transform.SetParent(root.transform,false);go.layer=island.layer;
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        go.AddComponent<MeshCollider>().sharedMesh=mesh;Undo.RegisterCreatedObjectUndo(go,"Build Trail Surface");
    }
    private void Marker(GameObject root,string name,Vector2 point)
    {
        GameObject go=new GameObject(name);go.transform.SetParent(root.transform,false);
        go.transform.position=new Vector3(point.x,SurfaceY(point)+.15f,point.y);
        Undo.RegisterCreatedObjectUndo(go,"Create Trail Marker");
    }

    private sealed class Actor { public Transform transform;public string group;public Bounds bounds;public bool tree; }
    private int HideNature()
    {
        if(nature==null)return 0;
        var actors=new List<Actor>();
        foreach(string groupName in new[]{"LEGO Trees","LEGO Bushes","LEGO Rocks"})
        {
            Transform group=nature.transform.Find(groupName);if(group==null)continue;
            foreach(Transform prop in group)
            {
                if(!prop.gameObject.activeInHierarchy)continue;
                Bounds bounds;if(!BoundsOf(prop.gameObject,out bounds))continue;
                actors.Add(new Actor{transform=prop,group=groupName,bounds=bounds,tree=groupName=="LEGO Trees"});
            }
        }
        GameObject hidden=new GameObject(HiddenName);hidden.transform.SetParent(nature.transform,false);hidden.SetActive(false);
        Undo.RegisterCreatedObjectUndo(hidden,"Hide Nature On Trail");
        var targets=new Dictionary<string,Transform>();int count=0;
        foreach(Actor actor in actors)
        {
            float radius=new Vector2(actor.bounds.extents.x,actor.bounds.extents.z).magnitude;
            // A canopy may overhang the path. Clear the trunk, not the entire
            // leafy crown, so the trail still winds closely through the forest.
            if(actor.tree)radius=Mathf.Min(radius,Mathf.Max(.8f,actor.bounds.size.y*.08f));
            Vector2 p=XZ(actor.bounds.center);
            float distance=Mathf.Min(RouteDistance(p,first),RouteDistance(p,second));
            bool onTrail=distance<pathWidth*.5f+natureClearance+radius
                ||Vector2.Distance(p,endpoint)<finishRadius+natureClearance+radius;
            if(!onTrail)continue;
            Transform target;
            if(!targets.TryGetValue(actor.group,out target))
            {
                GameObject group=new GameObject(actor.group);group.transform.SetParent(hidden.transform,false);
                Undo.RegisterCreatedObjectUndo(group,"Create Hidden Nature Group");target=group.transform;targets.Add(actor.group,target);
            }
            Undo.SetTransformParent(actor.transform,target,"Clear Nature From Trail");count++;
        }
        return count;
    }

    private void RestoreNature()
    {
        if(nature==null)return;
        Transform hidden=nature.transform.Find(HiddenName);if(hidden==null)return;
        foreach(Transform group in hidden)
        {
            Transform target=nature.transform.Find(group.name);
            if(target==null)target=nature.transform;
            var children=new List<Transform>();foreach(Transform child in group)children.Add(child);
            foreach(Transform child in children)Undo.SetTransformParent(child,target,"Restore Trail Nature");
        }
        Undo.DestroyObjectImmediate(hidden.gameObject);
    }

    private void Remove()
    {
        if(island==null){status="Choose the round island first.";return;}
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Remove Island Trail");
        RestoreNature();GameObject root=FindRoot(island.scene,RootName);if(root!=null)Undo.DestroyObjectImmediate(root);
        Undo.CollapseUndoOperations(group);EditorSceneManager.MarkSceneDirty(island.scene);SceneView.RepaintAll();
        status="Trail removed and hidden nature restored. Ground, river and mountain kept. Save Ctrl+S.";
    }

    private void DrawMarkers(SceneView view)
    {
        if(!showMarkers||island==null||!island.scene.IsValid())return;
        GameObject root=FindRoot(island.scene,RootName);if(root==null)return;
        var style=new GUIStyle(EditorStyles.helpBox){fontSize=13,fontStyle=FontStyle.Bold};
        foreach(string name in new[]{"Start Point","Crossing Bank A","Crossing Bank B","End Point"})
        {
            Transform point=root.transform.Find(name);if(point==null)continue;
            Handles.color=name=="Start Point"?Color.green:name=="End Point"?Color.yellow:Color.cyan;
            Handles.DrawWireDisc(point.position,Vector3.up,1.2f);Handles.Label(point.position+Vector3.up*2f,name,style);
        }
        Transform a=root.transform.Find("Crossing Bank A"),b=root.transform.Find("Crossing Bank B");
        if(a!=null&&b!=null){Handles.color=Color.white;Handles.DrawDottedLine(a.position+Vector3.up*.5f,b.position+Vector3.up*.5f,5f);}
    }

    private static float RouteDistance(Vector2 p,List<Vector2> route)
    {float d=float.PositiveInfinity;for(int i=0;i<route.Count-1;i++)d=Mathf.Min(d,SegmentDistance(p,route[i],route[i+1]));return d;}
    private static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
    {Vector2 v=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,v)/Mathf.Max(v.sqrMagnitude,.000001f));return Vector2.Distance(p,a+t*v);}
    private static void Lengths(List<Vector2> points,List<float> lengths)
    {lengths.Clear();float s=0;for(int i=0;i<points.Count;i++){if(i>0)s+=Vector2.Distance(points[i-1],points[i]);lengths.Add(s);}}
    private static bool Intersects(Vector2 p,Vector2 p2,Vector2 q,Vector2 q2)
    {
        Vector2 r=p2-p,s=q2-q;float den=r.x*s.y-r.y*s.x;if(Mathf.Abs(den)<1e-7f)return false;
        Vector2 d=q-p;float t=(d.x*s.y-d.y*s.x)/den,u=(d.x*r.y-d.y*r.x)/den;
        return t>.00001f&&t<.99999f&&u>.00001f&&u<.99999f;
    }
    private Vector2 KeepInside(Vector2 p,float inset)
    {
        for(int pass=0;pass<6;pass++)for(int i=0;i<coast.Count;i++)
        {
            Vector2 a=coast[i],b=coast[(i+1)%coast.Count],v=b-a;
            float length=Mathf.Max(v.magnitude,.000001f),d=Cross(a,b,p)/length;
            if(d<inset)p+=new Vector2(-v.y,v.x)/length*(inset-d);
        }
        return p;
    }
    private float Shore(Vector2 p)
    {float d=float.PositiveInfinity;for(int i=0;i<coast.Count;i++){Vector2 a=coast[i],b=coast[(i+1)%coast.Count];d=Mathf.Min(d,Cross(a,b,p)/Mathf.Max((b-a).magnitude,.000001f));}return d;}
    private static float Cross(Vector2 a,Vector2 b,Vector2 p){return (b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);}
    private static Vector2 XZ(Vector3 p){return new Vector2(p.x,p.z);}
    private static GameObject FindRoot(Scene scene,string name)
    {foreach(GameObject root in scene.GetRootGameObjects())if(root.name==name)return root;return null;}
    private static bool BoundsOf(GameObject go,out Bounds bounds)
    {
        bounds=new Bounds();bool found=false;
        foreach(Renderer renderer in go.GetComponentsInChildren<Renderer>())
        {if(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer))continue;if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);}
        return found;
    }
    private static List<Vector2> Hull(Mesh mesh,Transform transform)
    {
        var unique=new HashSet<Vector2>();foreach(Vector3 v in mesh.vertices)unique.Add(XZ(transform.TransformPoint(v)));
        var points=new List<Vector2>(unique);points.Sort((a,b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y.CompareTo(b.y));
        var hull=new List<Vector2>();
        foreach(Vector2 p in points){while(hull.Count>=2&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
        int lower=hull.Count;
        for(int i=points.Count-2;i>=0;i--){Vector2 p=points[i];while(hull.Count>lower&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
        if(hull.Count>0)hull.RemoveAt(hull.Count-1);return hull;
    }

    private static string NewDirectory()
    {
        const string parent="Assets/Island Trail Generated";
        if(!AssetDatabase.IsValidFolder(parent))AssetDatabase.CreateFolder("Assets","Island Trail Generated");
        string folder=AssetDatabase.GenerateUniqueAssetPath(parent+"/Setup");AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));return folder;
    }
    private static Mesh Save(string folder,string name,Mesh mesh)
    {AssetDatabase.CreateAsset(mesh,folder+"/"+name+".asset");return mesh;}
    private static Material TrailMaterial(string folder)
    {
        bool urp=GraphicsSettings.currentRenderPipeline!=null;
        Shader shader=Shader.Find(urp?"Universal Render Pipeline/Lit":"Standard");
        if(shader==null)throw new InvalidOperationException("Ground-compatible Lit shader not found");
        // A small repeating tile texture gives the tan trail visible brick seams.
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false);
        var pixels=new Color[128*128];
        for(int y=0;y<128;y++)for(int x=0;x<128;x++)pixels[y*128+x]=(x<2||y<2)?new Color(.72f,.72f,.68f):Color.white;
        texture.SetPixels(pixels);texture.Apply();string path=folder+"/Trail Tiles.png";
        File.WriteAllBytes(path,texture.EncodeToPNG());DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(importer!=null){importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Bilinear;importer.mipmapEnabled=true;importer.anisoLevel=4;importer.SaveAndReimport();}
        Texture2D saved=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var material=new Material(shader);Color tan=new Color(.82f,.67f,.41f,1);
        if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",tan);
        if(material.HasProperty("_Color"))material.SetColor("_Color",tan);
        if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",saved);
        if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",saved);
        if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.18f);
        if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.18f);
        AssetDatabase.CreateAsset(material,folder+"/Tan Trail.mat");return material;
    }

    private sealed class RoadMesh
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<int> triangles=new List<int>();
        public int Vertex(Vector3 p,Vector2 tex){int n=vertices.Count;vertices.Add(p);uv.Add(tex);return n;}
        public Vector3 Position(int index){return vertices[index];}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {int n=Vertex(a,Vector2.zero);Vertex(b,Vector2.right);Vertex(c,Vector2.one);Vertex(d,Vector2.up);Triangle(n,n+1,n+2);Triangle(n,n+2,n+3);}
        public void Triangle(int a,int b,int c)
        {if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude<1e-12f)return;triangles.Add(a);triangles.Add(b);triangles.Add(c);}
        public Mesh Finish(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);
            mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
#endif
