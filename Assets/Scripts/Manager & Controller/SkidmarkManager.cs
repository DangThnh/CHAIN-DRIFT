using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SkidmarkManager : MonoBehaviour
{
    public static SkidmarkManager Instance;

    [Header("Settings")]
    public Material skidmarkMaterial;
    public int maxMarks = 1000;
    public float markWidth = 0.35f;
    public float groundOffset = 0.03f;
    public float minDistance = 0.2f;

    private Mesh mesh;
    private Vector3[] vertices;
    private Vector3[] normals;
    private Color32[] colors;
    private Vector2[] uvs;
    private int[] triangles;

    private int markIndex = 0;
    private bool meshUpdated = false;
    private int activeTriangleCount = 0; // Đếm số tam giác thực tế để không bị lỗi hút về tâm

    private struct MarkSection
    {
        public Vector3 pos;
        public Vector3 normal;
        public Vector3 posl;
        public Vector3 posr;
        public Color32 color;
        public int lastIndex;
    }
    private MarkSection[] marks;

    void Awake()
    {
        Instance = this;
        mesh = GetComponent<MeshFilter>().mesh = new Mesh();
        mesh.name = "Skidmark_Procedural_Mesh";

        // Tự ép Transform của Manager về gốc tọa độ chuẩn để an toàn tuyệt đối
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        if (skidmarkMaterial != null)
            GetComponent<MeshRenderer>().material = skidmarkMaterial;

        marks = new MarkSection[maxMarks];
        vertices = new Vector3[maxMarks * 4];
        normals = new Vector3[maxMarks * 4];
        colors = new Color32[maxMarks * 4];
        uvs = new Vector2[maxMarks * 4];
        triangles = new int[maxMarks * 6];
    }

    public int AddSkidMark(Vector3 worldPos, Vector3 worldNormal, float intensity, int lastIndex)
    {
        if (intensity <= 0f) return -1;
        intensity = Mathf.Clamp01(intensity);

        // CHÌA KHÓA 1: CHUYỂN TỌA ĐỘ THẾ GIỚI CỦA XE VỀ TỌA ĐỘ LOCAL CỦA MANAGER
        Vector3 pos = transform.InverseTransformPoint(worldPos);
        Vector3 normal = transform.InverseTransformDirection(worldNormal);

        if (lastIndex > 0)
        {
            float sqrDist = (pos - marks[lastIndex % maxMarks].pos).sqrMagnitude;
            if (sqrDist < minDistance * minDistance) return lastIndex;
        }

        int curIndex = markIndex % maxMarks;
        marks[curIndex].pos = pos + normal * groundOffset;
        marks[curIndex].normal = normal;
        marks[curIndex].color = new Color32(0, 0, 0, (byte)(intensity * 220));
        marks[curIndex].lastIndex = lastIndex;

        if (lastIndex != -1)
        {
            MarkSection lastSection = marks[lastIndex % maxMarks];
            Vector3 dir = (marks[curIndex].pos - lastSection.pos).normalized;
            Vector3 xDir = Vector3.Cross(dir, normal).normalized * (markWidth * 0.5f);

            marks[curIndex].posl = marks[curIndex].pos - xDir;
            marks[curIndex].posr = marks[curIndex].pos + xDir;

            if (lastSection.lastIndex == -1)
            {
                lastSection.posl = lastSection.pos - xDir;
                lastSection.posr = lastSection.pos + xDir;
                marks[lastIndex % maxMarks] = lastSection;
            }

            int vIndex = curIndex * 4;
            vertices[vIndex + 0] = lastSection.posl;
            vertices[vIndex + 1] = lastSection.posr;
            vertices[vIndex + 2] = marks[curIndex].posl;
            vertices[vIndex + 3] = marks[curIndex].posr;

            normals[vIndex + 0] = lastSection.normal;
            normals[vIndex + 1] = lastSection.normal;
            normals[vIndex + 2] = normal;
            normals[vIndex + 3] = normal;

            colors[vIndex + 0] = lastSection.color;
            colors[vIndex + 1] = lastSection.color;
            colors[vIndex + 2] = marks[curIndex].color;
            colors[vIndex + 3] = marks[curIndex].color;

            uvs[vIndex + 0] = new Vector2(0, 0);
            uvs[vIndex + 1] = new Vector2(1, 0);
            uvs[vIndex + 2] = new Vector2(0, 1);
            uvs[vIndex + 3] = new Vector2(1, 1);

            int tIndex = curIndex * 6;
            triangles[tIndex + 0] = vIndex + 0;
            triangles[tIndex + 1] = vIndex + 1;
            triangles[tIndex + 2] = vIndex + 2;

            triangles[tIndex + 3] = vIndex + 2;
            triangles[tIndex + 4] = vIndex + 1;
            triangles[tIndex + 5] = vIndex + 3;

            // CHÌA KHÓA 2: CHỈ VẼ ĐÚNG SỐ TAM GIÁC ĐANG CÓ (TRIỆT TIÊU LỖI HÚT VỀ TÂM)
            activeTriangleCount = Mathf.Min(activeTriangleCount + 6, maxMarks * 6);

            meshUpdated = true;
        }

        markIndex++;
        return curIndex;
    }

    void LateUpdate()
    {
        if (!meshUpdated) return;
        meshUpdated = false;

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors32 = colors;
        mesh.uv = uvs;

        // Chỉ đưa vào GPU số lượng tam giác thực tế
        mesh.SetTriangles(triangles, 0, activeTriangleCount, 0);

        mesh.RecalculateBounds();
    }
}