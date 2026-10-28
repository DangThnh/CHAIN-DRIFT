using UnityEngine;

// Gắn cùng object 'Track' (có TrackMeshBuilder). Đọc lại chính các waypoint đã dựng đường.
[RequireComponent(typeof(TrackMeshBuilder))]
public class TrackPath : MonoBehaviour
{
    public static TrackPath Instance { get; private set; }

    public float Length { get; private set; }

    Vector3[] pts;
    float[] cum;   // khoảng cách cộng dồn tới từng điểm mẫu

    void Awake()
    {
        Instance = this;
        TrackMeshBuilder b = GetComponent<TrackMeshBuilder>();
        if (b.waypointsParent == null || b.waypointsParent.childCount < 2)
        {
            Debug.LogError("[TrackPath] Thiếu waypoint.");
            enabled = false;
            return;
        }
        pts = b.SamplePath().ToArray();
        cum = new float[pts.Length];
        for (int i = 1; i < pts.Length; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        Length = cum[pts.Length - 1];
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Trả về quãng đường dọc đường tâm của vị trí 'pos'. 'hint' là đoạn gần nhất lần trước.
    public float ProjectDistance(Vector3 pos, ref int hint)
    {
        int last = pts.Length - 2;
        hint = Mathf.Clamp(hint, 0, last);
        SearchRange(pos, Mathf.Max(0, hint - 20), Mathf.Min(last, hint + 20), out int seg, out float u, out float sqr);
        if (sqr > 625f) // lệch quá 25m (vd hồi sinh xa): tìm lại toàn tuyến
            SearchRange(pos, 0, last, out seg, out u, out sqr);
        hint = seg;
        return cum[seg] + (cum[seg + 1] - cum[seg]) * u;
    }

    public Vector3 GetPoint(float d)
    {
        d = Mathf.Clamp(d, 0f, Length);
        int lo = 0, hi = pts.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (cum[mid] <= d) lo = mid; else hi = mid;
        }
        float seg = cum[hi] - cum[lo];
        float u = seg > 1e-6f ? (d - cum[lo]) / seg : 0f;
        return Vector3.Lerp(pts[lo], pts[hi], u);
    }

    void SearchRange(Vector3 pos, int from, int to, out int seg, out float u, out float sqr)
    {
        seg = from; u = 0f; sqr = float.MaxValue;
        for (int i = from; i <= to; i++)
        {
            Vector3 a = pts[i], ab = pts[i + 1] - a;
            float len2 = ab.sqrMagnitude;
            float k = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(pos - a, ab) / len2) : 0f;
            float d2 = (pos - (a + ab * k)).sqrMagnitude;
            if (d2 < sqr) { sqr = d2; seg = i; u = k; }
        }
    }
}