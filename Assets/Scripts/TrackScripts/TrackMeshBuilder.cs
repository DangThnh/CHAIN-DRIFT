using System.Collections.Generic;
using UnityEngine;

// Công cụ Editor: dựng mặt đường + tường chắn từ các waypoint. Chạy bằng chuột phải > Build Track.
public class TrackMeshBuilder : MonoBehaviour
{
    [Header("Input")]
    public Transform waypointsParent;

    [Header("Road")]
    public float roadWidth = 14f;
    public float sampleSpacing = 4f;     // Khoảng cách giữa 2 mặt cắt (mét)
    public Material roadMaterial;

    [Header("Walls")]
    public bool buildWalls = true;
    public float wallHeight = 2f;
    public float wallThickness = 0.8f;
    public Material wallMaterial;

    [Header("Layer")]
    public string drivableLayerName = "Drivable";

    class MeshData
    {
        public List<Vector3> verts = new List<Vector3>();
        public List<int> tris = new List<int>();

        // Tự lật chiều tam giác sao cho mặt trước hướng về 'outward'
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            {
                Vector3 t = b; b = d; d = t;
            }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
    }

    [ContextMenu("Build Track")]
    public void Build()
    {
        if (waypointsParent == null || waypointsParent.childCount < 2)
        {
            Debug.LogError("[TrackMeshBuilder] Cần gán waypointsParent có ít nhất 2 waypoint.");
            return;
        }
        int drivable = LayerMask.NameToLayer(drivableLayerName);
        if (drivable < 0)
        {
            Debug.LogError("[TrackMeshBuilder] Không tìm thấy layer '" + drivableLayerName + "'.");
            return;
        }

        ClearChild("Road");
        ClearChild("Walls");

        List<Vector3> pts = SamplePath();
        int n = pts.Count;
        Vector3[] rights = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 tan = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
            tan.y = 0f;
            if (tan.sqrMagnitude < 1e-6f) tan = Vector3.forward;
            rights[i] = Vector3.Cross(Vector3.up, tan.normalized); // luôn nằm ngang, không nghiêng đường
        }

        MeshData road = new MeshData();
        MeshData walls = new MeshData();
        float hw = roadWidth * 0.5f;

        for (int i = 0; i < n - 1; i++)
        {
            Vector3 La = pts[i] - rights[i] * hw, Ra = pts[i] + rights[i] * hw;
            Vector3 Lb = pts[i + 1] - rights[i + 1] * hw, Rb = pts[i + 1] + rights[i + 1] * hw;
            road.AddQuad(La, Lb, Rb, Ra, Vector3.up);

            if (!buildWalls) continue;
            foreach (int s in new[] { -1, 1 })
            {
                float inner = s * hw;
                float outer = s * (hw + wallThickness);
                Vector3 ib0 = pts[i] + rights[i] * inner + Vector3.down * 0.3f;
                Vector3 it0 = pts[i] + rights[i] * inner + Vector3.up * wallHeight;
                Vector3 ot0 = pts[i] + rights[i] * outer + Vector3.up * wallHeight;
                Vector3 ob0 = pts[i] + rights[i] * outer + Vector3.down * 0.3f;
                Vector3 ib1 = pts[i + 1] + rights[i + 1] * inner + Vector3.down * 0.3f;
                Vector3 it1 = pts[i + 1] + rights[i + 1] * inner + Vector3.up * wallHeight;
                Vector3 ot1 = pts[i + 1] + rights[i + 1] * outer + Vector3.up * wallHeight;
                Vector3 ob1 = pts[i + 1] + rights[i + 1] * outer + Vector3.down * 0.3f;

                walls.AddQuad(ib0, ib1, it1, it0, -rights[i] * s);   // mặt trong, hướng vào đường
                walls.AddQuad(it0, it1, ot1, ot0, Vector3.up);       // mặt trên
                walls.AddQuad(ot0, ot1, ob1, ob0, rights[i] * s);    // mặt ngoài
            }
        }

        CreateChild("Road", road, roadMaterial, drivable);
        if (buildWalls) CreateChild("Walls", walls, wallMaterial, 0); // Tường ở layer Default, KHÔNG phải Drivable
        Debug.Log("[TrackMeshBuilder] Đã dựng đường: " + n + " mặt cắt.");
    }

    List<Vector3> SamplePath()
    {
        int n = waypointsParent.childCount;
        Vector3[] p = new Vector3[n];
        for (int i = 0; i < n; i++) p[i] = waypointsParent.GetChild(i).position;

        List<Vector3> result = new List<Vector3>();
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, n - 1)];
            int count = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / sampleSpacing));
            for (int s = 0; s < count; s++)
                result.Add(CatmullRom(p0, p1, p2, p3, s / (float)count));
        }
        result.Add(p[n - 1]);
        return result;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void ClearChild(string childName)
    {
        Transform t = transform.Find(childName);
        if (t != null) DestroyImmediate(t.gameObject);
    }

    void CreateChild(string childName, MeshData d, Material mat, int layer)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.layer = layer;
        go.isStatic = true;

        List<Vector3> local = new List<Vector3>(d.verts.Count);
        for (int i = 0; i < d.verts.Count; i++) local.Add(transform.InverseTransformPoint(d.verts[i]));

        Mesh mesh = new Mesh { name = childName + "Mesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(local);
        mesh.SetTriangles(d.tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        if (mat != null) mr.sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    void OnDrawGizmos()
    {
        if (waypointsParent == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < waypointsParent.childCount; i++)
        {
            Vector3 a = waypointsParent.GetChild(i).position;
            Gizmos.DrawSphere(a, 1.5f);
            if (i + 1 < waypointsParent.childCount)
                Gizmos.DrawLine(a, waypointsParent.GetChild(i + 1).position);
        }
    }
}