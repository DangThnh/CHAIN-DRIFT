using UnityEngine;

public class DestructibleObject : MonoBehaviour
{
    [Header("Fracture Settings")]
    public GameObject fracturedPrefab;
    public float breakForce = 500f;
    public float explosionRadius = 5f;

    [Header("Requirement")]
    public string breakerTag = "Cargo";
    // Bỏ minImpactSpeed đi vì dùng Trigger không đo được lực va chạm ngay lập tức

    private bool isBroken = false;

    // DÙNG TRIGGER ĐỂ BẮT VA CHẠM TRƯỚC KHI VẬT LÝ KỊP PHẢN LỰC
    void OnTriggerEnter(Collider other)
    {
        if (isBroken) return;

        bool canBreakIt = false;

        // ĐIỀU KIỆN 1: Là khối Cargo
        if (other.CompareTag(breakerTag))
        {
            canBreakIt = true;
        }
        // ĐIỀU KIỆN 2: Là xe của người chơi đang Bọc Thép hoặc đang Khoan
        else
        {
            NetworkCarController car = other.GetComponentInParent<NetworkCarController>();
            if (car != null && (car.IsArmored || car.IsDrilling))
            {
                canBreakIt = true;
            }
            
        }

        if (canBreakIt)
        {
            // Lấy vận tốc của vật thể tông vào để ép gạch văng theo hướng đó
            Vector3 impactVel = Vector3.zero;
            Rigidbody hitRb = other.attachedRigidbody;
            if (hitRb != null) impactVel = hitRb.linearVelocity;

            // Điểm va chạm mô phỏng (lấy tọa độ của vật đụng vào)
            Vector3 impactPoint = other.ClosestPoint(transform.position);

            BreakIt(impactPoint, impactVel);
        }
    }

    void BreakIt(Vector3 impactPoint, Vector3 impactVelocity)
    {
        isBroken = true;

        GameObject fracturedObj = Instantiate(fracturedPrefab, transform.position, transform.rotation);

        Rigidbody[] debrisRbs = fracturedObj.GetComponentsInChildren<Rigidbody>();
        foreach (Rigidbody rb in debrisRbs)
        {
            rb.AddExplosionForce(breakForce, impactPoint, explosionRadius);

            // Ép mảnh vỡ bay theo hướng xe đang lao tới
            rb.linearVelocity = impactVelocity * 0.8f;

            // Giảm khối lượng gạch xuống cực nhẹ để xe lướt qua không bị kẹt
            rb.mass = 0.1f;

            rb.gameObject.AddComponent<DebrisOptimizer>();
        }

        Destroy(gameObject);
    }
}