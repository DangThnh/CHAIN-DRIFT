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
        if (!Object.HasStateAuthority || isHidden) return;

        // Dùng GetComponentInParent để dò tìm từ gầm xe lên tận gốc Root
        NetworkCarController car = other.GetComponentInParent<NetworkCarController>();

        if (car != null)
        {
            car.ActivateArmor();

            isHidden = true;
            hideTimer = respawnTime;
        }
        else
        {
            // Debug xem nó đụng trúng cái gì mà không tìm ra xe
            Debug.Log("Hộp chạm vào cái gì đó không phải là xe: " + other.gameObject.name);
        }
    }
}