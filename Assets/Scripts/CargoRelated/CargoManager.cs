using UnityEngine;
using Fusion;

public class CargoManager : NetworkBehaviour
{
    [Header("Cargo Stats")]
    public float maxHealth = 1000f;

    // Biến mạng lưu trữ máu, khi máu thay đổi sẽ báo cho mọi người biết
    [Networked] public float CurrentHealth { get; set; }

    [Header("Visuals")]
    public MeshRenderer cargoMesh; // Kéo cục Mesh hiển thị của thùng hàng vào đây
    public Material normalMat;
    public Material damagedMat;    // Material khi máu yếu (Đỏ nhấp nháy hoặc nứt nẻ)

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            CurrentHealth = maxHealth;
        }
    }

    // Hàm nhận sát thương (Ai đụng mạnh cũng làm nó mất máu)
    // RpcSources.All: Bất kỳ ai cũng có thể gọi hàm này. RpcTargets.StateAuthority: Chỉ Server mới trừ máu để chống Hack.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(float damage)
    {
        if (CurrentHealth <= 0) return; // Đã chết rồi thì thôi

        CurrentHealth -= damage;
        Debug.Log($">> CARGO BỊ TẤN CÔNG! Máu còn: {CurrentHealth}/{maxHealth} <<");

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            TriggerCargoDestroyed();
        }
    }

    void TriggerCargoDestroyed()
    {
        // LOGIC KHI PHE PHÁ ĐÁM THẮNG
        Debug.Log("<color=red><b>BÙMMMM! KHỐI HÀNG ĐÃ BỊ PHÁ HỦY! PHE PHÁ ĐÁM CHIẾN THẮNG!</b></color>");
        // Sau này ta sẽ gọi hiệu ứng Particle Nổ khổng lồ ở đây
    }

    public override void Render()
    {
        // Hiệu ứng đổi màu thùng hàng khi sắp chết (Dưới 30% máu)
        if (cargoMesh != null && normalMat != null && damagedMat != null)
        {
            float healthPercent = CurrentHealth / maxHealth;
            if (healthPercent <= 0.3f && cargoMesh.material != damagedMat)
            {
                cargoMesh.material = damagedMat;
            }
            else if (healthPercent > 0.3f && cargoMesh.material != normalMat)
            {
                cargoMesh.material = normalMat;
            }
        }
    }
}