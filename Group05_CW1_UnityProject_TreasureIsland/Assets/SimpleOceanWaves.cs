using UnityEngine;

// Attach to the Ocean Plane. Keep its Transform scale at (1, 1, 1).
// Creates a separate mesh at runtime; the original Plane asset stays intact.
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimpleOceanWaves : MonoBehaviour
{
    [Min(10f)] public float waterSize = 200f;
    [Range(20, 200)] public int segments = 120;
    [Min(0f)] public float waveHeight = 0.25f;
    [Min(1f)] public float waveLength = 12f;
    [Min(0f)] public float waveSpeed = 2f;

    private MeshFilter meshFilter;
    private Mesh originalMesh;
    private Mesh waterMesh;
    private Vector3[] baseVertices;
    private Vector3[] vertices;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        originalMesh = meshFilter.sharedMesh;

        int count = Mathf.Clamp(segments, 20, 200);
        int row = count + 1;
        float size = Mathf.Max(10f, waterSize);
        baseVertices = new Vector3[row * row];
        vertices = new Vector3[baseVertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[count * count * 6];

        for (int z = 0; z <= count; z++)
        {
            for (int x = 0; x <= count; x++)
            {
                int i = z * row + x;
                float u = (float)x / count;
                float v = (float)z / count;
                baseVertices[i] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
                vertices[i] = baseVertices[i];
                uv[i] = new Vector2(u, v);
            }
        }

        int index = 0;
        for (int z = 0; z < count; z++)
        {
            for (int x = 0; x < count; x++)
            {
                int i = z * row + x;
                triangles[index++] = i;
                triangles[index++] = i + row;
                triangles[index++] = i + 1;
                triangles[index++] = i + 1;
                triangles[index++] = i + row;
                triangles[index++] = i + row + 1;
            }
        }

        waterMesh = new Mesh { name = "Animated Ocean (Runtime)" };
        waterMesh.MarkDynamic();
        waterMesh.vertices = vertices;
        waterMesh.uv = uv;
        waterMesh.triangles = triangles;
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();
        meshFilter.sharedMesh = waterMesh;
    }

    private void Update()
    {
        float k = 2f * Mathf.PI / Mathf.Max(1f, waveLength);
        float travel = Time.time * waveSpeed;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = baseVertices[i];
            p.y = Mathf.Sin(k * (p.x - travel)) * waveHeight
                + Mathf.Sin(k * 0.7f * (p.z + travel * 0.8f)) * waveHeight * 0.35f;
            vertices[i] = p;
        }
        waterMesh.vertices = vertices;
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
        if (meshFilter != null && meshFilter.sharedMesh == waterMesh)
            meshFilter.sharedMesh = originalMesh;
        if (waterMesh != null)
            Destroy(waterMesh);
    }
}
