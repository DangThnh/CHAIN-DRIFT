using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;

// Gắn lên root PlayerCar. Tính lượng hiệu ứng cho 2 bánh sau, gửi sang DriftVFXHub (dùng chung mọi xe).
public class DriftSmoke : MonoBehaviour
{
    public enum ImpactMode { Stretch, FaceCamera, Ground, Upright }

    [Header("Công tắc (để so sánh hiệu năng, đổi được lúc đang chạy)")]
    public bool vfxEnabled = true;

    [Header("Materials (BẮT BUỘC gán, dùng shader ChainDrift/DriftToon)")]
    [FormerlySerializedAs("smokeMaterial")]
    public Material flatMaterial;   // Dùng cho răng cưa và impact
    public Material blobMaterial;   // Dùng cho khói. Trống = dùng flatMaterial (mất tông sáng tối)

    [Header("Màu")]
    public Color smokeLight = new Color(1f, 0.97f, 0.9f, 1f);
    public Color smokeShade = new Color(0.72f, 0.8f, 0.94f, 1f);
    public Color finColor = Color.white;
    public Color impactColor = Color.white;

    [Header("Dither cho khói")]
    [Range(2, 6)] public int smokeSteps = 3;
    [Range(0f, 1f)] public float smokeDitherStrength = 1f;
    [Range(1f, 4f)] public float ditherPixelScale = 1f;

    [Header("Quy đổi tốc độ")]
    public float minSpeedKmh = 25f;
    public float maxSpeedKmh = 120f;

    [Header("Lớp 1: khói metaball")]
    public Vector2 blobRadius = new Vector2(0.35f, 0.65f);
    public float blobSpacing = 0.8f;
    public float blobMinRate = 6f;
    public float blobLifetime = 0.7f;
    public float blobRise = 1.0f;
    public float blobSpread = 0.6f;
    public float blobDrag = 1.2f;
    [Range(0f, 1f)] public float blobInheritVelocity = 0.1f;
    public int maxBlobsPerWheel = 22;
    [Range(0.15f, 0.5f)] public float smokeGoo = 0.3f;
    public float smokeCell = 0.3f;
    public float smokeCullDistance = 80f;
    [Range(1, 4)] public int smokeUpdateEvery = 2;          // dựng lại mesh khói mỗi N frame. 1 = mỗi frame (nặng nhất)

    [Header("Lớp 2: răng cưa bám quỹ đạo vệt lốp")]
    public int finMinTeeth = 5;                              // số răng hiển thị ở tốc độ tối thiểu
    public int finMaxTeeth = 11;                             // ... ở tốc độ tối đa
    public float finMinSpacing = 0.3f;                       // khoảng cách tối thiểu (m) giữa hai răng
    public Vector2 toothLengthMul = new Vector2(0.95f, 1.35f);   // chiều dài răng = đoạn đường * hệ số (>1 để chồng nhẹ, san sát)
    public Vector2 toothHeight = new Vector2(0.35f, 0.7f);
    [Range(0f, 0.8f)] public float finOverhang = 0.35f;      // đỉnh nghiêng ra sau (đổi cần chạy lại game)
    public float finLifetime = 0.3f;
    public float finSideOffset = 0f;                         // 0 = đúng đường vệt lốp
    public float finLift = 0.04f;
    [Range(0f, 1f)] public float finDither = 0.6f;

    [Header("Lớp 3: impact kiểu manga")]
    public Texture2D impactTexture;                          // Trống = tự sinh (4 biến thể)
    public int impactSheetColumns = 1;
    public int impactSheetRows = 1;
    public ImpactMode impactMode = ImpactMode.Stretch;
    public float impactLengthScale = 1.8f;                   // chỉ cho Stretch
    public float impactVelocityScale = 0.04f;                // chỉ cho Stretch
    public Vector2 impactRotation = new Vector2(0f, 360f);   // góc xoay (độ), không dùng cho Stretch
    public int impactBurstOnStart = 2;
    public float impactRate = 6f;
    public Vector2 impactSize = new Vector2(0.9f, 1.5f);
    public float impactLifetime = 0.25f;
    public float impactBackSpeed = 6f;
    [Range(0f, 1f)] public float impactInheritVelocity = 0.6f;
    public float impactSideOffset = 0.1f;
    public float impactBackOffset = 0.35f;
    public float impactUpOffset = 0.3f;

    [Header("Chuyển động")]
    public bool steppedMotion = true;

    const int Wheels = 2;
    static readonly ProfilerMarker mkEmit = new ProfilerMarker("DriftVFX.Emit");

    DriftVFXHub hub;
    Rigidbody rb;
    readonly DriftVFXHub.SmokeCluster[] clusters = new DriftVFXHub.SmokeCluster[Wheels];
    readonly int[] lastFrame = new int[Wheels];
    readonly float[] accBlob = new float[Wheels];
    readonly float[] accImpact = new float[Wheels];
    readonly Vector3[] lastFin = new Vector3[Wheels];
    readonly bool[] hasFin = new bool[Wheels];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (!Connect()) enabled = false;
    }

    void OnDestroy()
    {
        for (int i = 0; i < Wheels; i++)
            if (clusters[i] != null) clusters[i].abandoned = true;   // hub tự xóa khi khói đã tan hết
    }

    bool Connect()
    {
        hub = DriftVFXHub.GetOrCreate(this);
        if (hub == null) return false;
        for (int i = 0; i < Wheels; i++) clusters[i] = hub.CreateCluster();
        return true;
    }

    static int Take(ref float acc, float add, int cap)
    {
        acc += add;
        int n = (int)acc;
        acc -= n;
        return Mathf.Min(n, cap);
    }

    static Vector3 Smear(Vector3 vel, float dt) { return -vel * (Random.value * dt); }

    // Gọi mỗi frame cho mỗi bánh sau đang trượt. wheel: 0 = sau trái, 1 = sau phải
    public void Emit(int wheel, Vector3 point, Vector3 normal, float intensity)
    {
        if (!vfxEnabled || wheel < 0 || wheel >= Wheels) return;
        if (hub == null && !Connect()) return;
        using (mkEmit.Auto()) EmitInternal(wheel, point, normal, intensity);
    }

    void EmitInternal(int wheel, Vector3 point, Vector3 normal, float intensity)
    {
        bool justStarted = Time.frameCount - lastFrame[wheel] > 1;
        lastFrame[wheel] = Time.frameCount;

        float dt = Time.deltaTime;
        float k = Mathf.Clamp01(intensity);
        Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
        float speed = vel.magnitude;
        float sr = Mathf.InverseLerp(minSpeedKmh, maxSpeedKmh, speed * 3.6f);   // 0..1

        Transform car = transform;
        float sideSign = wheel == 0 ? -1f : 1f;
        Vector3 side = car.right * sideSign;                     // hướng ra NGOÀI của bánh này

        // Hướng chuyển động thật của điểm chạm bánh (đúng cả khi lùi hoặc trượt ngang)
        Vector3 moveDir = Vector3.ProjectOnPlane(rb != null ? rb.GetPointVelocity(point) : Vector3.zero, normal);
        if (moveDir.sqrMagnitude < 0.25f)
        {
            float sgn = Vector3.Dot(vel, car.forward) >= 0f ? 1f : -1f;
            moveDir = Vector3.ProjectOnPlane(car.forward * sgn, normal);
        }
        moveDir.Normalize();
        Vector3 back = -moveDir;                                 // phía SAU so với chiều chuyển động
        if (back.sqrMagnitude < 0.5f) back = -car.forward;

        // ---- Lớp 1: khói. Phát theo quãng đường để đường khói liền ----
        float rate = Mathf.Max(speed / Mathf.Max(0.2f, blobSpacing), blobMinRate) * k;
        int n = Take(ref accBlob[wheel], rate * dt, 4);
        for (int i = 0; i < n; i++)
        {
            float r = Random.Range(blobRadius.x, blobRadius.y) * (0.6f + 0.4f * k);
            Vector3 pos = point + normal * (r * 0.7f) + Smear(vel, dt)
                        + side * Random.Range(0f, 0.25f) + Random.insideUnitSphere * 0.1f;
            Vector3 v = vel * blobInheritVelocity
                      + Vector3.up * (blobRise * Random.Range(0.6f, 1f))
                      + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * blobSpread;
            clusters[wheel].Add(pos, v, r, blobLifetime * Random.Range(0.85f, 1.15f));
        }

        // ---- Lớp 2: răng cưa nằm trên quỹ đạo vệt lốp ----
        // Mỗi răng phủ đoạn đường vừa đi qua, hướng về phía quá khứ (khi lùi, đoạn đó nằm phía trước xe).
        int teeth = Mathf.RoundToInt(Mathf.Lerp(finMinTeeth, finMaxTeeth, sr));
        float spacing = Mathf.Max(finMinSpacing, speed * finLifetime / Mathf.Max(1, teeth));
        if (justStarted || !hasFin[wheel])
        {
            lastFin[wheel] = point;
            hasFin[wheel] = true;
        }
        else
        {
            Vector3 seg = Vector3.ProjectOnPlane(lastFin[wheel] - point, normal);
            float d = seg.magnitude;
            if (d > spacing * 4f) lastFin[wheel] = point;        // nhảy xa (hồi sinh, mất tiếp đất): bắt đầu lại
            else if (d >= spacing)
            {
                Quaternion rot = Quaternion.LookRotation(seg / d, normal);   // Z = hướng về quá khứ, Y = vuông góc mặt đường
                float len = d * Random.Range(toothLengthMul.x, toothLengthMul.y);
                float h = Random.Range(toothHeight.x, toothHeight.y) * Mathf.Lerp(0.6f, 1f, k);
                Vector3 pos = point + side * finSideOffset + normal * finLift;
                hub.SpawnFin(pos, rot, new Vector3(1f, h, len), finLifetime * Random.Range(0.85f, 1.15f));
                lastFin[wheel] = point;
            }
        }

        // ---- Lớp 3: impact manga bắn về sau (theo chiều chuyển động) ----
        int total = (justStarted ? impactBurstOnStart : 0) + Take(ref accImpact[wheel], impactRate * k * k * dt, 3);
        for (int i = 0; i < total; i++)
        {
            Vector3 pos = point + side * impactSideOffset + normal * impactUpOffset + back * impactBackOffset
                        + Random.insideUnitSphere * 0.1f;
            Vector3 v = vel * impactInheritVelocity + back * (impactBackSpeed * Random.Range(0.8f, 1.2f));
            hub.SpawnImpact(pos, v, Random.Range(impactSize.x, impactSize.y),
                            Random.Range(impactRotation.x, impactRotation.y), impactLifetime);
        }
    }
}