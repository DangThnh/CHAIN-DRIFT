using UnityEngine;

public class DestructibleObject : MonoBehaviour
{
    [Header("Fracture Settings")]
    public GameObject fracturedPrefab;  // Kéo Prefab bức tường vỡ vào đây
    public float breakForce = 500f;     // Lực văng của các mảnh vỡ
    public float explosionRadius = 5f;  // Bán kính vụ nổ mảnh vỡ

    [Header("Requirement")]
    public string breakerTag = "Cargo"; // CHỈ CÓ CARGO mới phá được
    public float minImpactSpeed = 5f;   // (Tùy chọn) Cargo phải đi đủ nhanh mới vỡ tường

    private bool isBroken = false;

    void OnCollisionEnter(Collision collision)
    {
        // Chống gọi hàm 2 lần
        if (isBroken) return;

        // KIỂM TRA ĐIỀU KIỆN 1: Phải là khối Cargo
        if (collision.gameObject.CompareTag(breakerTag))
        {
            // KIỂM TRA ĐIỀU KIỆN 2: Tốc độ va chạm (Tạo cảm giác chân thực)
            float impactSpeed = collision.relativeVelocity.magnitude;

            if (impactSpeed >= minImpactSpeed)
            {
                BreakIt(collision.contacts[0].point, collision.relativeVelocity);
            }
        }
    }

    void BreakIt(Vector3 impactPoint, Vector3 impactVelocity)
    {
        isBroken = true;

        // 1. Sinh ra bức tường vỡ tại đúng vị trí và góc xoay của tường gốc
        GameObject fracturedObj = Instantiate(fracturedPrefab, transform.position, transform.rotation);

        // 2. Tìm tất cả các mảnh vỡ bên trong để áp dụng lực
        Rigidbody[] debrisRbs = fracturedObj.GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in debrisRbs)
        {
            // Áp dụng lực nổ từ điểm va chạm để các mảnh vỡ văng tung tóe
            rb.AddExplosionForce(breakForce, impactPoint, explosionRadius);

            // Ép thêm quán tính của xe hàng vào mảnh vỡ (xe lao tới trước thì gạch văng tới trước)
            rb.linearVelocity = impactVelocity * 0.5f;

            // THÊM SCRIPT TỐI ƯU VÀO TỪNG MẢNH VỠ (Cực kỳ quan trọng)
            rb.gameObject.AddComponent<DebrisOptimizer>();
        }

        // 3. Tiêu diệt bức tường nguyên vẹn
        Destroy(gameObject);
    }
}