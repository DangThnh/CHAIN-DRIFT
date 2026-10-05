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
        if (isBroken) return;

        bool canBreakIt = false;

        // ĐIỀU KIỆN 1: Nếu là Khối hàng (Cargo) đụng vào -> Cho phép đập vỡ
        if (collision.gameObject.CompareTag(breakerTag))
        {
            canBreakIt = true;
        }
        // ĐIỀU KIỆN 2: Nếu là Xe của người chơi đụng vào...
        else
        {
            // Kiểm tra xem chiếc xe đó có đang "Bọc Thép" không?
            NetworkCarController car = collision.gameObject.GetComponentInParent<NetworkCarController>();
            if (car != null && car.IsArmored)
            {
                canBreakIt = true;
            }
        }

        // TIẾN HÀNH ĐẬP VỠ NẾU ĐẠT ĐIỀU KIỆN
        if (canBreakIt)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;

            // Xe bọc thép húc tường thường bị giảm tốc, ta hạ chuẩn tốc độ vỡ xuống một chút cho sướng
            if (impactSpeed >= (minImpactSpeed * 0.5f))
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