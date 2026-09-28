using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class TowController : MonoBehaviour
{
    [Header("Towing Settings")]
    public float attachRadius = 15f;    // Tầm xa tối đa để có thể quăng dây
    public float maxRopeLength = 10f;   // Độ dài tối đa của sợi dây khi căng
    public float springForce = 5000f;   // Lực kéo của dây
    public float springDamper = 500f;   // Độ dập dao động của dây

    [Header("Visual Settings")]
    public Transform ropeFirePoint;     // Điểm bắt đầu của dây (Đuôi xe của cậu)
    public int linePoints = 10;         // Độ mượt của sợi dây (chia làm bao nhiêu đốt)
    public float sagMultiplier = 2f;    // Độ thõng xuống khi dây chùng
    //public float chainLinkSize = 1f; // Độ dài của 1 mắt xích (Chỉnh để xích to/nhỏ)

    private Rigidbody myRb;
    private SpringJoint activeJoint;
    private Transform attachedTarget;
    private LineRenderer lineRenderer;

    void Start()
    {
        myRb = GetComponent<Rigidbody>();

        // Setup Line Renderer
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = linePoints;
        lineRenderer.enabled = false;
    }

    void Update()
    {
        // Bấm phím F để Bắn/Gỡ dây
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (activeJoint == null)
                TryAttachRope();
            else
                DetachRope();
        }

        DrawRope();
    }

    void TryAttachRope()
    {
        // Quét tìm xe hàng xung quanh
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, attachRadius);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Cargo") && hit.attachedRigidbody != null)
            {
                // Nếu tìm thấy, tạo kết nối SpringJoint
                attachedTarget = hit.transform;

                activeJoint = gameObject.AddComponent<SpringJoint>();
                activeJoint.connectedBody = hit.attachedRigidbody;

                // === THÊM DÒNG NÀY ĐỂ BẬT LẠI VA CHẠM ===
                activeJoint.enableCollision = true;

                // Thiết lập thông số vật lý của dây
                activeJoint.autoConfigureConnectedAnchor = false;
                activeJoint.connectedAnchor = Vector3.zero; // Gắn vào tâm xe hàng (Hoặc cậu có thể chỉnh tùy ý)

                activeJoint.maxDistance = maxRopeLength;
                activeJoint.minDistance = 0f;
                activeJoint.spring = springForce;
                activeJoint.damper = springDamper;

                lineRenderer.enabled = true;

                Debug.Log("Đã kết nối với xe hàng!");
                break; // Chỉ gắn 1 xe
            }
        }
    }

    void DetachRope()
    {
        if (activeJoint != null)
        {
            Destroy(activeJoint);
            attachedTarget = null;
            lineRenderer.enabled = false;
            Debug.Log("Đã tháo dây!");
        }
    }

    void DrawRope()
    {
        if (activeJoint == null || attachedTarget == null || ropeFirePoint == null) return;

        Vector3 startPos = ropeFirePoint.position;
        Vector3 endPos = attachedTarget.position;

        // Tính toán độ chùng của dây
        float currentDistance = Vector3.Distance(startPos, endPos);

        // XÍCH KHÔNG BỊ GIÃN
        // Nó sẽ tự động scale lặp lại texture dựa trên chiều dài dây
        //lineRenderer.material.mainTextureScale = new Vector2(currentDistance / chainLinkSize, 1f);

        // Nếu khoảng cách lớn hơn độ dài tối đa -> Dây căng (Độ chùng = 0)
        // Nếu khoảng cách nhỏ -> Dây chùng xuống
        float currentSag = 0f;
        if (currentDistance < maxRopeLength)
        {
            // Tỷ lệ chùng tỷ lệ nghịch với khoảng cách
            float slackRatio = 1f - (currentDistance / maxRopeLength);
            currentSag = slackRatio * sagMultiplier;
        }

        // Vẽ đường cong Parabola giả bằng LineRenderer
        for (int i = 0; i < linePoints; i++)
        {
            float t = i / (float)(linePoints - 1); // t chạy từ 0 đến 1

            // Nội suy tuyến tính từ điểm đầu đến điểm cuối
            Vector3 pointPos = Vector3.Lerp(startPos, endPos, t);

            // Thêm độ võng xuống (t * (1-t) tạo ra đường parabol trũng ở giữa)
            float sagAtPoint = Mathf.Sin(t * Mathf.PI) * currentSag;
            pointPos.y -= sagAtPoint;

            lineRenderer.SetPosition(i, pointPos);
        }
    }
}