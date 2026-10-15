using UnityEngine;
using Fusion;

public class PowerUpBox : NetworkBehaviour
{
    public float respawnTime = 10f;
    [Networked] private NetworkBool isHidden { get; set; }
    private float hideTimer = 0f;

    private MeshRenderer meshRenderer;
    private Collider col;

    public override void Spawned()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        col = GetComponent<Collider>();
    }

    public override void FixedUpdateNetwork()
    {
        // Chỉ Server mới lo vụ Respawn
        if (Object.HasStateAuthority && isHidden)
        {
            hideTimer -= Runner.DeltaTime;
            if (hideTimer <= 0)
            {
                isHidden = false;
            }
        }
    }

    public override void Render()
    {
        // Tất cả mọi máy đều ẩn/hiện hộp quà theo biến isHidden
        meshRenderer.enabled = !isHidden;
    }

    void OnTriggerEnter(Collider other)
    {
        // Chỉ Host xử lý ăn hộp
        if (!Object.HasStateAuthority || isHidden) return;

        NetworkCarController car = other.GetComponentInParent<NetworkCarController>();

        if (car != null)
        {
            // TÌM SCRIPT KÉO HÀNG CỦA CHIẾC XE NÀY
            NetworkTowController towCtrl = car.GetComponent<NetworkTowController>();

            // BÍ QUYẾT FIX LỖI: Kiểm tra xem biến AttachedCargo có đang rỗng không
            // Nếu không rỗng (Tức là ĐANG KÉO HÀNG) -> TỪ CHỐI CHO ĂN!
            if (towCtrl != null && towCtrl.AttachedCargo != null)
            {
                Debug.Log(">> HỘP QUÀ TỪ CHỐI: XE NÀY ĐANG BẬN KÉO HÀNG! <<");
                return; // Thoát hàm ngay lập tức
            }

            // Đạt điều kiện: Xe tự do -> Cho ăn!
            car.ActivateArmor();

            // Ẩn hộp đi
            isHidden = true;
            hideTimer = respawnTime;
        }
    }
}