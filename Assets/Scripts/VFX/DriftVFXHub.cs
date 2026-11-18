using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

// Khói metaball (1 mesh động cho mọi xe) + hệ hạt răng cưa + hệ hạt impact. DÙNG CHUNG cho mọi xe.
// Chỉ là hiệu ứng cục bộ, không đồng bộ mạng.
public class DriftVFXHub : MonoBehaviour
{
    public static DriftVFXHub Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    const int MaxFin = 320, MaxImpact = 96;
    const int MaxPoints = 16000;   // điểm lưới tối đa cho MỘT nhóm khói
    const int MaxVerts = 60000;    // đỉnh tối đa cho TOÀN BỘ khói mỗi frame

    // ---------------- Khối khói của một bánh ----------------
    public struct Blob { public Vector3 pos, vel; public float age, life, r0; }

    public class SmokeCluster
    {
        public Blob[] b;
        public int n;
        public bool abandoned;

        public SmokeCluster(int cap) { b = new Blob[cap]; }

        public void Add(Vector3 pos, Vector3 vel, float r0, float life)
        {
            Blob nb = new Blob { pos = pos, vel = vel, r0 = r0, life = life };
            if (n < b.Length) { b[n++] = nb; return; }
            int o = 0; float ma = -1f;                       // đầy: thay khối già nhất
            for (int i = 0; i < n; i++) if (b[i].age > ma) { ma = b[i].age; o = i; }
            b[o] = nb;
        }

        public void Step(float dt, float drag)
        {
            float damp = Mathf.Max(0f, 1f - drag * dt);
            for (int i = n - 1; i >= 0; i--)
            {
                b[i].pos += b[i].vel * dt;
                b[i].vel *= damp;
                b[i].age += dt;
                if (b[i].age >= b[i].life) { b[i] = b[n - 1]; n--; }
            }
        }
    }

    ParticleSystem finPS, impactPS;
    Material smokeMat, finMat, impactMat;
    Mesh smokeMesh, finMesh;
    Texture2D ownedTex;
    int lastVertCount;
    static readonly ProfilerMarker mkSmoke = new ProfilerMarker("DriftVFX.BuildSmokeMesh");
    int smokeEvery = 2, frameCounter;

    readonly List<SmokeCluster> clusters = new List<SmokeCluster>();
    readonly List<Vector3> verts = new List<Vector3>(16384);
    readonly List<Vector3> norms = new List<Vector3>(16384);
    readonly List<int> tris = new List<int>(16384);
    readonly List<Vector3> gv = new List<Vector3>(2048);
    readonly float[] field = new float[MaxPoints];
    readonly int[] cellVert = new int[MaxPoints];
    readonly float[] cvals = new float[8];
    static readonly int[] EdgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
    static readonly int[] EdgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

    float[] rad, infl;
    int[] parent, members;
    int blobCap;
    float cell, iso, isoRatio, drag, cullDist;
    Camera cam;

    public static DriftVFXHub GetOrCreate(DriftSmoke cfg)
    {
        if (Instance != null) return Instance;
        if (cfg == null || cfg.flatMaterial == null)
        {
            Debug.LogWarning("[DriftVFX] Chưa gán Flat Material trên DriftSmoke, bỏ qua hiệu ứng khói.");
            return null;
        }
        var go = new GameObject("DriftVFXHub");
        var hub = go.AddComponent<DriftVFXHub>();
        hub.Build(cfg);
        Instance = hub;
        return hub;
    }

    public SmokeCluster CreateCluster()
    {
        var c = new SmokeCluster(blobCap);
        clusters.Add(c);
        return c;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (smokeMesh != null) Destroy(smokeMesh);
        if (finMesh != null) Destroy(finMesh);
        if (smokeMat != null) Destroy(smokeMat);
        if (finMat != null) Destroy(finMat);
        if (impactMat != null) Destroy(impactMat);
        if (ownedTex != null) Destroy(ownedTex);
    }

    static void SetF(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
    static void SetC(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

    // ---------------- Dựng ----------------
    void Build(DriftSmoke cfg)
    {
        blobCap = Mathf.Clamp(cfg.maxBlobsPerWheel, 4, 60);
        smokeEvery = Mathf.Clamp(cfg.smokeUpdateEvery, 1, 4);
        cell = Mathf.Clamp(cfg.smokeCell, 0.15f, 1.2f);
        iso = Mathf.Clamp(cfg.smokeGoo, 0.15f, 0.5f);
        isoRatio = Mathf.Sqrt(1f - Mathf.Pow(iso, 1f / 3f));   // bán kính hiển thị / bán kính ảnh hưởng của một khối
        drag = cfg.blobDrag;
        cullDist = Mathf.Max(10f, cfg.smokeCullDistance);
        rad = new float[blobCap]; infl = new float[blobCap];
        parent = new int[blobCap]; members = new int[blobCap];

        // Khói
        smokeMat = cfg.blobMaterial != null ? new Material(cfg.blobMaterial) : new Material(cfg.flatMaterial);
        SetC(smokeMat, "_BaseColor", cfg.smokeLight);
        SetC(smokeMat, "_ShadeColor", cfg.smokeShade);
        SetF(smokeMat, "_LitAmount", 1f);
        SetF(smokeMat, "_Steps", cfg.smokeSteps);
        SetF(smokeMat, "_DitherStrength", cfg.smokeDitherStrength);
        SetF(smokeMat, "_DitherScale", cfg.ditherPixelScale);
        SetF(smokeMat, "_DissolveAmount", 0f);

        var go = new GameObject("DriftSmokeMesh");
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = smokeMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        smokeMesh = new Mesh { name = "DriftSmoke", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        smokeMesh.MarkDynamic();
        mf.sharedMesh = smokeMesh;

        // Răng cưa
        finMat = new Material(cfg.flatMaterial);
        SetC(finMat, "_BaseColor", cfg.finColor);
        SetF(finMat, "_LitAmount", 0f);
        SetF(finMat, "_DissolveAmount", cfg.finDither);
        finMesh = MakeFinMesh(cfg.finOverhang);
        finPS = MakeSystem("Fin", finMat, MaxFin, ParticleSystemRenderMode.Mesh, finMesh, true);
        SetSize(finPS, Curve(cfg.steppedMotion, new[] { 0f, 0.7f, 0.9f }, new[] { 1f, 0.7f, 0.35f }));

        // Impact
        Texture2D tex = cfg.impactTexture;
        int cols = Mathf.Max(1, cfg.impactSheetColumns), rows = Mathf.Max(1, cfg.impactSheetRows);
        if (tex == null) { ownedTex = MakeSplatSheet(); tex = ownedTex; cols = 2; rows = 2; }
        impactMat = new Material(cfg.flatMaterial);
        if (impactMat.HasProperty("_BaseMap")) impactMat.SetTexture("_BaseMap", tex); else impactMat.mainTexture = tex;
        SetC(impactMat, "_BaseColor", cfg.impactColor);
        SetF(impactMat, "_LitAmount", 0f);
        SetF(impactMat, "_DissolveAmount", 0f);

        ParticleSystemRenderMode impactRender;
        switch (cfg.impactMode)
        {
            case DriftSmoke.ImpactMode.FaceCamera: impactRender = ParticleSystemRenderMode.Billboard; break;
            case DriftSmoke.ImpactMode.Ground: impactRender = ParticleSystemRenderMode.HorizontalBillboard; break;
            case DriftSmoke.ImpactMode.Upright: impactRender = ParticleSystemRenderMode.VerticalBillboard; break;
            default: impactRender = ParticleSystemRenderMode.Stretch; break;
        }
        impactPS = MakeSystem("Impact", impactMat, MaxImpact, impactRender, null, false);
        var ir = impactPS.GetComponent<ParticleSystemRenderer>();
        ir.lengthScale = cfg.impactLengthScale;
        ir.velocityScale = cfg.impactVelocityScale;

        SetSize(impactPS, Curve(cfg.steppedMotion, new[] { 0f, 0.5f, 0.8f }, new[] { 1f, 0.8f, 0.4f }));
        if (cols * rows > 1)
        {
            var ts = impactPS.textureSheetAnimation;
            ts.enabled = true;
            ts.mode = ParticleSystemAnimationMode.Grid;
            ts.numTilesX = cols;
            ts.numTilesY = rows;
            ts.animation = ParticleSystemAnimationType.WholeSheet;
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);   // chọn ngẫu nhiên một ô
            ts.cycleCount = 1;
        }
    }

    ParticleSystem MakeSystem(string name, Material mat, int max, ParticleSystemRenderMode mode, Mesh mesh, bool is3D)
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
        main.startSize3D = is3D;
        main.startRotation3D = is3D;

        var em = ps.emission; em.enabled = false;     // chỉ phát bằng Emit() từ script
        var shape = ps.shape; shape.enabled = false;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        if (mesh != null) { r.mesh = mesh; r.alignment = ParticleSystemRenderSpace.World; }
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

    public void SpawnFin(Vector3 pos, Quaternion rot, Vector3 size, float life)
    {
        var p = new ParticleSystem.EmitParams();
        p.position = pos;
        p.velocity = Vector3.zero;
        p.rotation3D = rot.eulerAngles;
        p.startSize3D = size;
        p.startLifetime = life;
        finPS.Emit(p, 1);
    }

    public void SpawnImpact(Vector3 pos, Vector3 vel, float size, float rotDeg, float life)
    {
        var p = new ParticleSystem.EmitParams();
        p.position = pos;
        p.velocity = vel;
        p.startSize = size;
        p.rotation = rotDeg;
        p.startLifetime = life;
        impactPS.Emit(p, 1);
    }

    // ---------------- Cập nhật và dựng khói ----------------
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = clusters.Count - 1; i >= 0; i--)
        {
            SmokeCluster c = clusters[i];
            c.Step(dt, drag);
            if (c.abandoned && c.n == 0) clusters.RemoveAt(i);
        }
        frameCounter++;
        if (lastVertCount == 0 || frameCounter % smokeEvery == 0)
        {
            using (mkSmoke.Auto()) BuildSmokeMesh();
        }
    }

    static float Profile(float u)
    {
        u = Mathf.Clamp01(u);
        float grow = Mathf.Lerp(0.5f, 1.25f, Mathf.Clamp01(u / 0.3f));
        float f = Mathf.Clamp01((u - 0.45f) / 0.55f);
        return grow * (1f - f * f);     // phình rồi co về 0 ở cuối đời
    }

    void BuildSmokeMesh()
    {
        verts.Clear(); norms.Clear(); tris.Clear();
        if (cam == null) cam = Camera.main;
        bool useCull = cam != null;
        Vector3 camPos = useCull ? cam.transform.position : Vector3.zero;
        float cull2 = cullDist * cullDist;

        for (int i = 0; i < clusters.Count; i++)
        {
            if (clusters[i].n == 0) continue;
            BuildCluster(clusters[i], useCull, camPos, cull2);
            if (verts.Count > MaxVerts) break;
        }

        if (verts.Count == 0)
        {
            if (lastVertCount > 0) { smokeMesh.Clear(); lastVertCount = 0; }
            return;
        }
        lastVertCount = verts.Count;
        smokeMesh.Clear();
        smokeMesh.SetVertices(verts);
        smokeMesh.SetNormals(norms);
        smokeMesh.SetTriangles(tris, 0, false);
        smokeMesh.bounds = new Bounds(Vector3.zero, new Vector3(4000f, 4000f, 4000f));
    }

    int Find(int x)
    {
        while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
        return x;
    }

    void BuildCluster(SmokeCluster c, bool useCull, Vector3 camPos, float cull2)
    {
        int n = c.n, alive = 0;
        for (int i = 0; i < n; i++)
        {
            Blob bl = c.b[i];
            rad[i] = bl.r0 * Profile(bl.age / bl.life);
            infl[i] = rad[i] / isoRatio;
            parent[i] = i;
            if (rad[i] >= 0.08f) alive++;
        }
        if (alive == 0) return;
        if (useCull && (c.b[0].pos - camPos).sqrMagnitude > cull2) return;

        // Nhóm các khối có vùng ảnh hưởng chạm nhau (union-find)
        for (int i = 0; i < n; i++)
        {
            if (rad[i] < 0.08f) continue;
            for (int j = 0; j < i; j++)
            {
                if (rad[j] < 0.08f) continue;
                float s = infl[i] + infl[j];
                if ((c.b[i].pos - c.b[j].pos).sqrMagnitude < s * s)
                {
                    int ri = Find(i), rj = Find(j);
                    if (ri != rj) parent[ri] = rj;
                }
            }
        }

        for (int root = 0; root < n; root++)
        {
            if (rad[root] < 0.08f || Find(root) != root) continue;
            int m = 0;
            for (int i = 0; i < n; i++)
                if (rad[i] >= 0.08f && Find(i) == root) members[m++] = i;
            MeshGroup(c, m);
            if (verts.Count > MaxVerts) return;
        }
    }

    int Farthest(SmokeCluster c, int from, int m)
    {
        int best = from; float bd = -1f;
        for (int q = 0; q < m; q++)
        {
            float d = (c.b[members[q]].pos - c.b[from].pos).sqrMagnitude;
            if (d > bd) { bd = d; best = members[q]; }
        }
        return best;
    }

    // Dựng bề mặt metaball của một nhóm bằng naive surface nets trên lưới xoay theo chiều dài nhóm
    void MeshGroup(SmokeCluster c, int m)
    {
        int fa = Farthest(c, members[0], m);
        int fb = Farthest(c, fa, m);
        Vector3 d = c.b[fb].pos - c.b[fa].pos; d.y = 0f;
        Vector3 U = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
        Vector3 W = Vector3.up;
        Vector3 V = Vector3.Cross(W, U);

        float mnU = float.MaxValue, mnV = float.MaxValue, mnW = float.MaxValue;
        float mxU = float.MinValue, mxV = float.MinValue, mxW = float.MinValue;
        for (int q = 0; q < m; q++)
        {
            int bi = members[q];
            Vector3 p = c.b[bi].pos; float R = infl[bi];
            float lu = Vector3.Dot(p, U), lv = Vector3.Dot(p, V), lw = Vector3.Dot(p, W);
            mnU = Mathf.Min(mnU, lu - R); mxU = Mathf.Max(mxU, lu + R);
            mnV = Mathf.Min(mnV, lv - R); mxV = Mathf.Max(mxV, lv + R);
            mnW = Mathf.Min(mnW, lw - R); mxW = Mathf.Max(mxW, lw + R);
        }

        float cs = cell;
        int nx = 0, ny = 0, nz = 0;
        bool ok = false;
        for (int it = 0; it < 14 && !ok; it++)       // nhóm quá dài: tự tăng cỡ ô để không vượt giới hạn
        {
            nx = Mathf.CeilToInt((mxU - mnU) / cs) + 3;
            ny = Mathf.CeilToInt((mxV - mnV) / cs) + 3;
            nz = Mathf.CeilToInt((mxW - mnW) / cs) + 3;
            if ((long)nx * ny * nz <= MaxPoints) ok = true; else cs *= 1.2f;
        }
        if (!ok) return;

        float oU = mnU - cs, oV = mnV - cs, oW = mnW - cs;
        Vector3 origin = U * oU + V * oV + W * oW;
        int nxy = nx * ny, total = nxy * nz;
        System.Array.Clear(field, 0, total);

        // Cộng trường của từng khối vào lưới
        for (int q = 0; q < m; q++)
        {
            int bi = members[q];
            Vector3 p = c.b[bi].pos; float R = infl[bi], R2 = R * R, Rc = R / cs;
            float cu = (Vector3.Dot(p, U) - oU) / cs, cv = (Vector3.Dot(p, V) - oV) / cs, cw = (Vector3.Dot(p, W) - oW) / cs;
            int i0 = Mathf.Max(0, Mathf.FloorToInt(cu - Rc)), i1 = Mathf.Min(nx - 1, Mathf.CeilToInt(cu + Rc));
            int j0 = Mathf.Max(0, Mathf.FloorToInt(cv - Rc)), j1 = Mathf.Min(ny - 1, Mathf.CeilToInt(cv + Rc));
            int k0 = Mathf.Max(0, Mathf.FloorToInt(cw - Rc)), k1 = Mathf.Min(nz - 1, Mathf.CeilToInt(cw + Rc));
            for (int k = k0; k <= k1; k++)
            {
                float dz = (k - cw) * cs, dz2 = dz * dz;
                if (dz2 >= R2) continue;
                for (int j = j0; j <= j1; j++)
                {
                    float dy = (j - cv) * cs, d2yz = dz2 + dy * dy;
                    if (d2yz >= R2) continue;
                    int row = nx * (j + ny * k);
                    for (int i = i0; i <= i1; i++)
                    {
                        float dx = (i - cu) * cs, d2 = d2yz + dx * dx;
                        if (d2 >= R2) continue;
                        float qv = 1f - d2 / R2;
                        field[row + i] += qv * qv * qv;
                    }
                }
            }
        }

        // Mỗi ô có giao cắt bề mặt: một đỉnh ở trung bình các giao điểm trên cạnh
        gv.Clear();
        for (int idx = 0; idx < total; idx++) cellVert[idx] = -1;
        for (int k = 0; k < nz - 1; k++)
            for (int j = 0; j < ny - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int b0 = i + nx * (j + ny * k);
                    cvals[0] = field[b0]; cvals[1] = field[b0 + 1];
                    cvals[2] = field[b0 + nx]; cvals[3] = field[b0 + nx + 1];
                    cvals[4] = field[b0 + nxy]; cvals[5] = field[b0 + nxy + 1];
                    cvals[6] = field[b0 + nxy + nx]; cvals[7] = field[b0 + nxy + nx + 1];
                    int mask = 0;
                    for (int cr = 0; cr < 8; cr++) if (cvals[cr] >= iso) mask |= 1 << cr;
                    if (mask == 0 || mask == 255) continue;

                    float sx = 0f, sy = 0f, sz = 0f; int cnt = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        int a = EdgeA[e], b = EdgeB[e];
                        bool ia = ((mask >> a) & 1) == 1, ib = ((mask >> b) & 1) == 1;
                        if (ia == ib) continue;
                        float t = (iso - cvals[a]) / (cvals[b] - cvals[a]);
                        sx += (a & 1) + t * ((b & 1) - (a & 1));
                        sy += ((a >> 1) & 1) + t * (((b >> 1) & 1) - ((a >> 1) & 1));
                        sz += ((a >> 2) & 1) + t * (((b >> 2) & 1) - ((a >> 2) & 1));
                        cnt++;
                    }
                    float inv = 1f / cnt;
                    cellVert[b0] = gv.Count;
                    gv.Add(origin + U * ((i + sx * inv) * cs) + V * ((j + sy * inv) * cs) + W * ((k + sz * inv) * cs));
                }

        // Mỗi cạnh lưới có đổi dấu: một mặt nối 4 đỉnh của 4 ô quanh cạnh
        for (int k = 1; k < nz - 1; k++)
            for (int j = 1; j < ny - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                {
                    int p0 = i + nx * (j + ny * k);
                    bool a = field[p0] >= iso;
                    if (a != (field[p0 + 1] >= iso))
                        EmitQuad(cellVert[p0 - nx - nxy], cellVert[p0 - nxy], cellVert[p0], cellVert[p0 - nx], a ? U : -U);
                    if (a != (field[p0 + nx] >= iso))
                        EmitQuad(cellVert[p0 - 1 - nxy], cellVert[p0 - nxy], cellVert[p0], cellVert[p0 - 1], a ? V : -V);
                    if (a != (field[p0 + nxy] >= iso))
                        EmitQuad(cellVert[p0 - 1 - nx], cellVert[p0 - nx], cellVert[p0], cellVert[p0 - 1], a ? W : -W);
                }
    }

    void EmitQuad(int a, int b, int c, int d, Vector3 outward)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0) return;
        Vector3 p0 = gv[a], p1 = gv[b], p2 = gv[c], p3 = gv[d];
        AddTri(p0, p1, p2, outward);
        AddTri(p0, p2, p3, outward);
    }

    // Đỉnh không dùng chung + pháp tuyến phẳng => mặt phẳng rõ ràng (low-poly)
    void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float sq = n.sqrMagnitude;
        if (sq < 1e-10f) return;
        if (Vector3.Dot(n, outward) < 0f) { Vector3 t = b; b = c; c = t; n = -n; }
        n *= 1f / Mathf.Sqrt(sq);
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        norms.Add(n); norms.Add(n); norms.Add(n);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
    }

    // ---------------- Mesh và texture sinh bằng code ----------------
    // Răng cưa: 1 tam giác đứng trong mặt phẳng Y-Z. Đáy dọc Z (0..1), đỉnh ở y=1 và lệch ra sau (z = 1 + overhang).
    // Shader Cull Off nên thấy cả hai mặt. UV.y = chiều cao (dùng cho dither tan dần ở đỉnh).
    static Mesh MakeFinMesh(float overhang)
    {
        overhang = Mathf.Clamp(overhang, 0f, 0.8f);
        var m = new Mesh { name = "DriftFin" };
        m.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 1f + overhang) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f) };
        m.normals = new[] { Vector3.right, Vector3.right, Vector3.right };
        m.triangles = new[] { 0, 1, 2 };
        m.RecalculateBounds();
        return m;
    }

    // Sheet 2x2 hình impact kiểu manga: vành gai ngẫu nhiên, tâm rỗng. Point filter (răng cưa chủ ý).
    static Texture2D MakeSplatSheet()
    {
        const int T = 64, S = 2;
        var tex = new Texture2D(T * S, T * S, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[T * S * T * S];
        var clear = new Color32(255, 255, 255, 0);
        var white = new Color32(255, 255, 255, 255);

        for (int tile = 0; tile < S * S; tile++)
        {
            var rnd = new System.Random(1234 + tile * 77);
            int count = 11 + rnd.Next(0, 5);
            float[] sa = new float[count], sl = new float[count], sw = new float[count];
            for (int s = 0; s < count; s++)
            {
                sa[s] = (s + ((float)rnd.NextDouble() - 0.5f) * 0.7f) * 360f / count;
                sl[s] = 0.62f + (float)rnd.NextDouble() * 0.36f;
                sw[s] = 7f + (float)rnd.NextDouble() * 9f;
            }
            float phase = (float)rnd.NextDouble() * 6.28f;
            int ox = (tile % S) * T, oy = (tile / S) * T;
            float c = (T - 1) * 0.5f;
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    bool inside = false;
                    if (dist <= 1f)
                    {
                        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        float outer = 0.42f;
                        for (int s = 0; s < count; s++)
                        {
                            float f = 1f - Mathf.Abs(Mathf.DeltaAngle(ang, sa[s])) / sw[s];
                            if (f > 0f) outer = Mathf.Max(outer, 0.42f + f * (sl[s] - 0.42f));
                        }
                        float inner = 0.2f + 0.07f * Mathf.Sin(ang * Mathf.Deg2Rad * 5f + phase);
                        inside = dist <= outer && dist >= inner;
                    }
                    px[(oy + y) * (T * S) + ox + x] = inside ? white : clear;
                }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }
}