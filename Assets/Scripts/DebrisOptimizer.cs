using UnityEngine;

public class DebrisOptimizer : MonoBehaviour
{
    public float lifeTime = 4f;         // Tồn tại 4 giây
    public float sinkSpeed = 1.5f;      // Tốc độ lún xuống đất

    private Rigidbody rb;
    private Collider col;
    private bool isSinking = false;
    private float timer = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    void Update()
    {
        timer += Time.deltaTime;

        // Giai đoạn 1: Sau 4 giây, vô hiệu hóa vật lý và bắt đầu lún
        if (timer >= lifeTime && !isSinking)
        {
            isSinking = true;
            Destroy(rb);  // Tắt Rigidbody -> Giải phóng 90% CPU
            Destroy(col); // Tắt Collider -> Giải phóng bộ nhớ va chạm
        }

        // Giai đoạn 2: Lún dần xuống lòng đất để biến mất mượt mà (Không bị giật chớp tắt)
        if (isSinking)
        {
            transform.Translate(Vector3.down * sinkSpeed * Time.deltaTime, Space.World);

            // Xóa sổ hoàn toàn sau khi lún xong
            if (timer >= lifeTime + 2f)
            {
                Destroy(gameObject);
            }
        }
    }
}