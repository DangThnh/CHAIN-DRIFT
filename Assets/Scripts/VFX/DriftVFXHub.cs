using System.Collections.Generic;
using UnityEngine;

// 3 hệ hạt DÙNG CHUNG cho mọi xe (tự tạo lúc chạy). Chỉ là hiệu ứng cục bộ, không đồng bộ mạng.
public class DriftVFXHub : MonoBehaviour
{
    public static DriftVFXHub Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    const int MaxBlob = 96, MaxJag = 96, MaxSpike = 96;   // giới hạn cứng cho TOÀN BỘ xe

    ParticleSystem blobPS, jagPS, spikePS;
    Material flatInst, blobInst;
    Texture2D tex;
    Mesh blobMesh, spikeMesh;

    public static DriftVFXHub GetOrCreate(Material flat, Material blob, bool stepped)
    {
        if (Instance != null) return Instance;
        if (flat == null)
        {
            Debug.LogWarning("[DriftVFX] Chưa gán Flat Material trên DriftSmoke, bỏ qua hiệu ứng khói.");
            return null;
        }
        var go = new GameObject("DriftVFXHub");
        var hub = go.AddComponent<DriftVFXHub>();
        hub.Build(flat, blob, stepped);
        Instance = hub;
        return hub;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (tex != null) Destroy(tex);
        if (flatInst != null) Destroy(flatInst);
        if (blobInst != null) Destroy(blobInst);
        if (blobMesh != null) Destroy(blobMesh);
        if (spikeMesh != null) Destroy(spikeMesh);
    }

    // ---------------- Dựng 3 hệ hạt ----------------
    void Build(Material flat, Material blob, bool stepped)
    {
        tex = MakeJagTexture();
        flatInst = new Material(flat);
        if (flatInst.HasProperty("_BaseMap")) flatInst.SetTexture("_BaseMap", tex);
        else flatInst.mainTexture = tex;

        if (blob != null) blobInst = new Material(blob);
        Material blobMat = blobInst != null ? blobInst : flatInst;

        blobMesh = MakeBlobMesh();
        spikeMesh = MakeSpikeMesh();

        blobPS = MakeSystem("Blob", ParticleSystemRenderMode.Mesh, blobMat, MaxBlob, blobMesh, true);
        jagPS = MakeSystem("Jag", ParticleSystemRenderMode.HorizontalBillboard, flatInst, MaxJag, null, false);
        spikePS = MakeSystem("Spike", ParticleSystemRenderMode.Mesh, flatInst, MaxSpike, spikeMesh, false);
        spikePS.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Velocity;

        SetSize(blobPS, Curve(stepped, new[] { 0f, 0.2f, 0.45f, 0.7f, 0.9f }, new[] { 0.55f, 0.95f, 1.25f, 1f, 0.35f }));
        SetSize(jagPS, Curve(stepped, new[] { 0f, 0.33f, 0.66f }, new[] { 0.5f, 0.9f, 1.2f }));
        SetSize(spikePS, AnimationCurve.Linear(0f, 1f, 1f, 0.05f));
    }

    ParticleSystem MakeSystem(string name, ParticleSystemRenderMode mode, Material mat, int max, Mesh mesh, bool rot3D)
    {
        var go = new GameObject("DriftVFX_" + name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = 1f;
        main.startSize = 1f;
        main.maxParticles = max;
        main.startRotation3D = rot3D;

        var em = ps.emission; em.enabled = false;     // chỉ phát bằng Emit() từ script
        var shape = ps.shape; shape.enabled = false;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        if (mesh != null) r.mesh = mesh;
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        ps.Play();
        return ps;
    }

    static void SetSize(ParticleSystem ps, AnimationCurve c)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, c);
    }

    static AnimationCurve Curve(bool stepped, float[] t, float[] v)
    {
        var keys = new Keyframe[t.Length];
        for (int i = 0; i < t.Length; i++)
            keys[i] = stepped ? new Keyframe(t[i], v[i], 0f, float.PositiveInfinity) : new Keyframe(t[i], v[i]);
        var c = new AnimationCurve(keys);
        if (!stepped) for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
        return c;
    }

    // ---------------- API phát hạt ----------------
    public void EmitBlob(Vector3 pos, Vector3 vel, float size, float life, Color col)
    {
        var p = new ParticleSystem.EmitParams();
        p.position = pos; p.velocity = vel; p.startSize = size; p.startLifetime = life; p.startColor = col;
        p.rotation3D = new Vector3(Random.value * 360f, Random.value * 360f, Random.value * 360f);
        blobPS.Emit(p, 1);
    }

    public void EmitJag(Vector3 pos, Vector3 vel, float size, float life, float rotDeg, Color col)
    {
        var p = new ParticleSystem.EmitParams();
        p.position = pos; p.velocity = vel; p.startSize = size; p.startLifetime = life; p.startColor = col;
        p.rotation = rotDeg;
        jagPS.Emit(p, 1);
    }

    public void EmitSpike(Vector3 pos, Vector3 vel, float size, float life, Color col)
    {
        var p = new ParticleSystem.EmitParams();
        p.position = pos; p.velocity = vel; p.startSize = size; p.startLifetime = life; p.startColor = col;
        spikePS.Emit(p, 1);
    }

    // ---------------- Texture và mesh sinh bằng code ----------------
    // Hình sao 9 gai, 32x32, Point filter (răng cưa chủ ý). Tâm luôn đặc nên UV (0.5, 0.5) luôn hiện.
    static Texture2D MakeJagTexture()
    {
        const int N = 32;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c;
                float a = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;
                float tri = 1f - Mathf.Abs(2f * ((a * 9f) % 1f) - 1f);   // sóng tam giác = gai nhọn
                float r = Mathf.Lerp(0.38f, 0.98f, tri);
                px[y * N + x] = d <= r ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }

    // Mọi UV đặt ở (0.5, 0.5) để dùng chung texture với quad răng cưa
    class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        // Tự lật chiều tam giác để mặt trước hướng ra xa 'center'
        public void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 center)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0f) { Vector3 t = b; b = c; c = t; }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            uvs.Add(new Vector2(0.5f, 0.5f)); uvs.Add(new Vector2(0.5f, 0.5f)); uvs.Add(new Vector2(0.5f, 0.5f));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();     // đỉnh không dùng chung nên mặt phẳng (faceted)
            m.RecalculateBounds();
            return m;
        }
    }

    // Khối 20 mặt (icosahedron), đường kính 1
    static Mesh MakeBlobMesh()
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] v =
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        int[] f =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        var b = new MeshBuilder();
        for (int i = 0; i < f.Length; i += 3)
            b.AddTri(v[f[i]].normalized * 0.5f, v[f[i + 1]].normalized * 0.5f, v[f[i + 2]].normalized * 0.5f, Vector3.zero);
        return b.Build("DriftBlob");
    }

    // Kim tự tháp mảnh dọc trục +Z, dài 1
    static Mesh MakeSpikeMesh()
    {
        const float r = 0.1f;
        Vector3 tip = new Vector3(0, 0, 1f);
        Vector3 p0 = new Vector3(0f, r, 0f);
        Vector3 p1 = new Vector3(r * 0.866f, -r * 0.5f, 0f);
        Vector3 p2 = new Vector3(-r * 0.866f, -r * 0.5f, 0f);
        Vector3 center = new Vector3(0, 0, 0.25f);
        var b = new MeshBuilder();
        b.AddTri(tip, p0, p1, center);
        b.AddTri(tip, p1, p2, center);
        b.AddTri(tip, p2, p0, center);
        b.AddTri(p0, p1, p2, center);
        return b.Build("DriftSpike");
    }
}