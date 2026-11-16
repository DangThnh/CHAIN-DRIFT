using UnityEngine;
using UnityEngine.Serialization;

// Gắn lên root PlayerCar. Tính lượng hạt cho 2 bánh sau, gửi sang DriftVFXHub (dùng chung mọi xe).
public class DriftSmoke : MonoBehaviour
{
    [Header("Materials (BẮT BUỘC gán)")]
    [FormerlySerializedAs("smokeMaterial")]
    public Material flatMaterial;   // URP/Particles/Unlit, Opaque + Alpha Clipping. Dùng cho lớp 2 và 3
    public Material blobMaterial;   // URP/Particles/Simple Lit, Opaque. Dùng cho lớp 1. Trống = dùng flatMaterial (khói phẳng)

    [Header("Palette")]
    public Color smokeLight = new Color(1f, 0.97f, 0.9f, 1f);
    public Color smokeShade = new Color(0.78f, 0.84f, 0.95f, 1f);
    public Color spikeColor = Color.white;

    [Header("Lớp 1: khối khói")]
    public float blobRate = 14f;                       // hạt/giây mỗi bánh (ở cường độ tối đa)
    public Vector2 blobSize = new Vector2(0.55f, 0.95f);
    public float blobLifetime = 0.7f;
    public float blobRise = 1.1f;
    public float blobSpread = 0.7f;
    [Range(0f, 1f)] public float blobInheritVelocity = 0.15f;

    [Header("Lớp 2: răng cưa bám mặt đường")]
    public float jagRate = 24f;
    public Vector2 jagSize = new Vector2(1.1f, 1.8f);
    public float jagLifetime = 0.28f;
    public float jagSpeed = 2.2f;

    [Header("Lớp 3: gai nhọn tại bánh")]
    public float spikeRate = 12f;
    public int spikeBurstOnStart = 6;                  // chùm gai khi bánh vừa bắt đầu trượt
    public Vector2 spikeSize = new Vector2(0.7f, 1.2f);
    public float spikeLifetime = 0.2f;
    public float spikeSpeed = 7f;

    [Header("Chuyển động")]
    public bool steppedMotion = true;                  // đổi kích thước theo nấc, kiểu retro

    const int Wheels = 2;
    DriftVFXHub hub;
    Rigidbody rb;
    readonly int[] lastFrame = new int[Wheels];
    readonly float[] accBlob = new float[Wheels];
    readonly float[] accJag = new float[Wheels];
    readonly float[] accSpike = new float[Wheels];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        hub = DriftVFXHub.GetOrCreate(flatMaterial, blobMaterial, steppedMotion);
        if (hub == null) enabled = false;
    }

    static int Take(ref float acc, float add)
    {
        acc += add;
        int n = (int)acc;
        acc -= n;
        return Mathf.Min(n, 6);   // chặn đột biến khi frame dài
    }

    // Rải vị trí hạt dọc quãng đường xe đã đi trong frame, tránh khói đứt thành từng cụm khi chạy nhanh
    static Vector3 Smear(Vector3 vel, float dt) { return -vel * (Random.value * dt); }

    // Gọi mỗi frame cho mỗi bánh sau đang trượt. wheel: 0 = sau trái, 1 = sau phải
    public void Emit(int wheel, Vector3 point, Vector3 normal, float intensity)
    {
        if (wheel < 0 || wheel >= Wheels) return;
        if (hub == null)
        {
            hub = DriftVFXHub.GetOrCreate(flatMaterial, blobMaterial, steppedMotion);
            if (hub == null) return;
        }

        bool justStarted = Time.frameCount - lastFrame[wheel] > 1;
        lastFrame[wheel] = Time.frameCount;

        float dt = Time.deltaTime;
        float k = Mathf.Clamp01(intensity);
        Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;

        // Lớp 1: khối khói
        int n = Take(ref accBlob[wheel], blobRate * k * dt);
        for (int i = 0; i < n; i++)
        {
            Vector3 pos = point + normal * 0.35f + Smear(vel, dt) + Random.insideUnitSphere * 0.15f;
            Vector3 v = vel * blobInheritVelocity
                      + Vector3.up * (blobRise * Random.Range(0.6f, 1f))
                      + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * blobSpread;
            hub.EmitBlob(pos, v, Random.Range(blobSize.x, blobSize.y), blobLifetime,
                         Color.Lerp(smokeLight, smokeShade, Random.value));
        }

        // Lớp 2: răng cưa bám mặt đường
        n = Take(ref accJag[wheel], jagRate * k * dt);
        for (int i = 0; i < n; i++)
        {
            Vector3 pos = point + normal * 0.06f + Smear(vel, dt);
            Vector3 dir = Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized;
            Vector3 v = dir * (jagSpeed * Random.Range(0.5f, 1f)) + vel * 0.1f;
            hub.EmitJag(pos, v, Random.Range(jagSize.x, jagSize.y), jagLifetime,
                        Random.Range(0f, 360f), smokeLight);
        }

        // Lớp 3: gai nhọn (chùm khi bắt đầu trượt + thưa dần khi cường độ cao)
        n = Take(ref accSpike[wheel], spikeRate * k * k * dt) + (justStarted ? spikeBurstOnStart : 0);
        for (int i = 0; i < n; i++)
        {
            Vector3 d = Random.onUnitSphere;
            d.y = Mathf.Abs(d.y) * 0.8f + 0.25f;
            Vector3 pos = point + normal * 0.15f + Smear(vel, dt);
            Vector3 v = d.normalized * (spikeSpeed * Random.Range(0.6f, 1f)) + vel * 0.3f;
            hub.EmitSpike(pos, v, Random.Range(spikeSize.x, spikeSize.y), spikeLifetime, spikeColor);
        }
    }
}