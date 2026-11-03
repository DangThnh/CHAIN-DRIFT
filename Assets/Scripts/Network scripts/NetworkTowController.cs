using Fusion;
//using UnityEditor.EditorTools;
using UnityEngine;

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

    [Networked] public TickTimer TowTimer { get; set; }

    public void ServerRelease()
    {
        if (Object.HasStateAuthority) DetachRope();
    }

    public void ServerSetTimer(float seconds)
    {
        TowTimer = TickTimer.CreateFromSeconds(Runner, seconds);
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
        NetworkCarController myCar = GetComponent<NetworkCarController>();
        if (myCar != null && myCar.IsDead) return;
        TowManager tm = TowManager.Instance;

        if (activeJoint == null)
        {
            if (tm != null && !tm.ServerCanAttach(myCar)) return;
            if (TryAttachRope() && tm != null) tm.ServerOnAttached(myCar);
        }
        else
        {
            if (tm != null && !tm.ServerCanDetach(myCar))
            {
                Debug.Log("[TOW] Người kéo duy nhất không được tháo dây.");
                return;
            }
            DetachRope();
        }
    }

    bool TryAttachRope()
    {
        NetworkCarController myCar = GetComponent<NetworkCarController>();
        if (myCar != null && myCar.MyTeam == NetworkCarController.PlayerTeam.Hunter)
        {
            Debug.Log(">> TỪ CHỐI: Phe Phá Đám không được phép kéo hàng! <<");
            return false;
        }

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, attachRadius);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Cargo") && hit.attachedRigidbody != null)
            {
                NetworkObject cargoNetObj = hit.GetComponentInParent<NetworkObject>();
                if (cargoNetObj == null) continue;

                AttachedCargo = cargoNetObj;

                activeJoint = gameObject.AddComponent<SpringJoint>();
                activeJoint.connectedBody = hit.attachedRigidbody;
                activeJoint.autoConfigureConnectedAnchor = false;
                activeJoint.connectedAnchor = Vector3.zero;
                activeJoint.anchor = Vector3.zero;
                activeJoint.maxDistance = maxRopeLength;
                activeJoint.minDistance = 3f;
                activeJoint.spring = springForce;
                activeJoint.damper = springDamper;
                activeJoint.enableCollision = true;

                if (myCar != null) myCar.IsTowingCargo = true;
                Debug.Log(">> SERVER: ĐÃ KẾT NỐI DÂY XÍCH THÀNH CÔNG! <<");
                return true;
            }
        }
        return false;
    }

    void DetachRope()
    {
        if (activeJoint != null)
        {
            Destroy(activeJoint);
            activeJoint = null;   // để lần nối tiếp theo trong cùng frame không bị nhầm
        }
        AttachedCargo = null;
        TowTimer = default;

        NetworkCarController car = GetComponent<NetworkCarController>();
        if (car != null) car.IsTowingCargo = false;
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

    [Rpc(RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    public void RPC_ForceDetach()
    {
        DetachRope();
    }
}