using UnityEngine;
using Fusion;

[RequireComponent(typeof(LineRenderer))]
public class NetworkTowController : NetworkBehaviour
{
    [Header("Towing Settings")]
    public float attachRadius = 15f;
    public float maxRopeLength = 12f;
    public float springForce = 8000f;
    public float springDamper = 1500f;

    [Header("Visual Settings")]
    public Transform ropeFirePoint;
    public int linePoints = 10;
    public float sagMultiplier = 2f;
    public float chainLinkSize = 1f;

    [Networked] public NetworkObject AttachedCargo { get; set; }

    private SpringJoint activeJoint;
    private LineRenderer lineRenderer;
    private bool lastTowButtonState = false;

    public override void Spawned()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = linePoints;
        lineRenderer.enabled = false;

        // ÉP BUỘC DÂY XÍCH VÀ BIẾN MẠNG PHẢI TRỐNG RỖNG LÚC MỚI SINH RA
        AttachedCargo = null;
        if (activeJoint != null) Destroy(activeJoint);
    }

    public override void FixedUpdateNetwork()
    {
        // Chỉ thằng nào lái xe mới có quyền bấm nút
        if (HasInputAuthority == false) return;

        if (GetInput(out NetworkInputData data))
        {
            bool isDown = data.isTowPressed && !lastTowButtonState;
            lastTowButtonState = data.isTowPressed;

            if (isDown)
            {
                // Bấm phím F -> Gửi tín hiệu cho Server xử lý nối dây
                RPC_ToggleRope();
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ToggleRope()
    {
        // Phải kiểm tra biến activeJoint thay vì biến mạng
        if (activeJoint == null)
            TryAttachRope();
        else
            DetachRope();
    }

    void TryAttachRope()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, attachRadius);
        foreach (var hit in hitColliders)
        {
            // Kiểm tra chặt: Phải là Cargo, có Rigidbody, và KHÔNG ĐƯỢC CHỨA SPRING JOINT NÀO KHÁC
            if (hit.CompareTag("Cargo") && hit.attachedRigidbody != null)
            {
                NetworkObject cargoNetObj = hit.GetComponentInParent<NetworkObject>();

                if (cargoNetObj != null)
                {
                    AttachedCargo = cargoNetObj;

                    // Tạo Joint mới 100% bằng code
                    activeJoint = gameObject.AddComponent<SpringJoint>();
                    activeJoint.connectedBody = hit.attachedRigidbody;

                    // NẾU LỖI VẪN XẢY RA, LÀ DO 2 DÒNG NÀY ĐANG ĐÁNH LỘN VỚI NHAU:
                    // Ta phải set Anchor về 0 trước, rồi mới bật Auto Configure
                    activeJoint.autoConfigureConnectedAnchor = false;
                    activeJoint.connectedAnchor = Vector3.zero; // Gắn vào giữa khối hàng
                    activeJoint.anchor = Vector3.zero; // Gắn vào giữa thân xe kéo

                    // Thiết lập vật lý dây
                    activeJoint.maxDistance = maxRopeLength;
                    activeJoint.minDistance = 3f; // Chống hút dính: Không cho kéo quá gần 5 mét
                    activeJoint.spring = springForce;
                    activeJoint.damper = springDamper;
                    activeJoint.enableCollision = true;

                    Debug.Log(">> SERVER: ĐÃ KẾT NỐI DÂY XÍCH THÀNH CÔNG! <<");
                    break;
                }
            }
        }
    }

    void DetachRope()
    {
        if (activeJoint != null)
        {
            Destroy(activeJoint);
        }
        AttachedCargo = null;
        Debug.Log(">> SERVER: ĐÃ THÁO DÂY XÍCH! <<");
    }

    public override void Render()
    {
        if (AttachedCargo != null && ropeFirePoint != null)
        {
            lineRenderer.enabled = true;
            DrawRope(ropeFirePoint.position, AttachedCargo.transform.position);
        }
        else
        {
            lineRenderer.enabled = false;
        }
    }

    void DrawRope(Vector3 startPos, Vector3 endPos)
    {
        float currentDistance = Vector3.Distance(startPos, endPos);

        if (lineRenderer.material != null)
            lineRenderer.material.mainTextureScale = new Vector2(currentDistance / chainLinkSize, 1f);

        float currentSag = 0f;
        if (currentDistance < maxRopeLength)
        {
            float slackRatio = 1f - (currentDistance / maxRopeLength);
            currentSag = slackRatio * sagMultiplier;
        }

        for (int i = 0; i < linePoints; i++)
        {
            float t = i / (float)(linePoints - 1);
            Vector3 pointPos = Vector3.Lerp(startPos, endPos, t);
            pointPos.y -= Mathf.Sin(t * Mathf.PI) * currentSag;
            lineRenderer.SetPosition(i, pointPos);
        }
    }
}