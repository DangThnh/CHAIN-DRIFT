using UnityEngine;

public class ArcadeCarController : MonoBehaviour
{
    [Header("Skid Marks (Vệt lốp)")]
    public float skidThreshold = 2.5f;  // Lực văng ngang để tạo vệt
    public float roadOffset = 0.05f;    // Nâng vệt lên 5cm để không bị chìm vào mặt đường (Chống Z-fighting)

    // Tạo một cấu trúc để nối vệt bánh với điểm phuộc nhún
    [System.Serializable]
    public struct SkidEmitter
    {
        public Transform rayPoint;          // Kéo điểm RL_Point hoặc RR_Point vào đây
        public TrailRenderer trailRenderer; // Kéo cục Skid tương ứng vào đây
    }
    public SkidEmitter[] skidEmitters;

    [Header("References")]
    public Transform[] rayPoints;
    public Transform[] wheelMeshes;
    public LayerMask drivableLayer;

    [Header("Engine & Speed")]
    public float acceleration = 2500f;
    public float maxSpeedKmh = 120f;
    public float reverseForce = 1500f;
    public float autoBrakeForce = 500f;

    [Header("Suspension (Phuộc nhún)")]
    public float suspensionRestDist = 0.6f;
    public float springStrength = 35000f;
    public float springDamper = 4500f;

    [Header("Chassis Visuals (Hiệu ứng Vỏ xe)")]
    public Transform chassisMesh;
    public float maxRollAngle = 10f;
    public float maxPitchAngle = 5f;
    public float chassisSmoothSpeed = 8f;

    [Header("Visual Dynamics (Độ trễ & Quán tính)")]
    public float rollBuildUpSpeed = 2.5f;   // Tốc độ nghiêng dần khi giữ nút rẽ
    public float pitchDecaySpeed = 3f;      // Tốc độ giảm dần hiệu ứng giật ga/phanh
    public float sustainedPitchRatio = 0.2f;// Giữ lại bao nhiêu % độ chúi/ngóc khi chạy đều (0.2 = 20%)

    private float currentRoll = 0f;
    private float currentPitch = 0f;

    // Biến nội bộ xử lý quán tính
    private float visualSteerInput = 0f;
    private float pitchInertiaMultiplier = 0f;
    private float lastMoveInput = 0f;

    // THÔNG SỐ MỚI: Tầng đệm chống lún
    [Header("Anti-Bottoming Out")]
    [Tooltip("Dời điểm bắn Raycast lên cao bao nhiêu mét để không bị chìm xuống đất")]
    public float rayOriginOffset = 0.5f;
    [Tooltip("Bán kính bánh xe 3D để model không chìm xuống đường")]
    public float wheelRadius = 0.3f;

    [Header("Steering")]
    public float turnStrength = 1500f;
    public float maxWheelTurnAngle = 35f;
    public AnimationCurve turnCurve;

    [Header("Traction & Drift")]
    [Range(0f, 1f)] public float normalGrip = 0.95f;
    [Range(0f, 1f)] public float driftGrip = 0.2f;

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isDrifting;
    private int groundedWheels;
    private float currentSpeedKmh;
    private float currentWheelSpin = 0f; // Biến lưu trữ góc lăn của bánh xe

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.5f, 0);
    }

    void Update()
    {
        GetInputs();
        UpdateWheelVisuals();
        UpdateChassisVisuals();
        HandleSkidMarks();
    }

    void FixedUpdate()
    {
        currentSpeedKmh = rb.linearVelocity.magnitude * 3.6f;
        groundedWheels = 0;

        for (int i = 0; i < rayPoints.Length; i++)
        {
            // NÂNG ĐIỂM BẮN LÊN CAO: rayPoint + (Up * offset)
            Vector3 rayStartPoint = rayPoints[i].position + (transform.up * rayOriginOffset);

            // TIA BẮN PHẢI DÀI HƠN: Độ dài lò xo + Offset nâng lên + 0.2f padding
            float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

            RaycastHit hit;
            if (Physics.Raycast(rayStartPoint, -transform.up, out hit, maxRayLength, drivableLayer))
            {
                groundedWheels++;

                // Tính toán khoảng cách thực tế từ Raypoint ban đầu đến mặt đất
                // Bằng cách lấy khoảng cách tia Raycast trừ đi phần offset đã nâng lên
                float actualDistanceFromRaypoint = hit.distance - rayOriginOffset;

                float offset = suspensionRestDist - actualDistanceFromRaypoint;
                float velocityAtPoint = Vector3.Dot(transform.up, rb.GetPointVelocity(rayPoints[i].position));

                float force = (offset * springStrength) - (velocityAtPoint * springDamper);

                rb.AddForceAtPosition(transform.up * force, rayPoints[i].position);
            }
        }

        if (groundedWheels > 0)
        {
            HandleMovement();
            HandleSteering();
            HandleTraction();
        }
    }

    // ... [Các hàm GetInputs, HandleMovement, HandleSteering, HandleTraction giữ nguyên như cũ] ...
    void GetInputs()
    {
        moveInput = 0f;
        if (Input.GetKey(KeyCode.W)) moveInput = 1f;
        if (Input.GetKey(KeyCode.S)) moveInput = -1f;

        steerInput = 0f;
        if (Input.GetKey(KeyCode.A)) steerInput = -1f;
        if (Input.GetKey(KeyCode.D)) steerInput = 1f;

        isDrifting = Input.GetKey(KeyCode.Space);
    }

    void HandleMovement()
    {
        Vector3 forwardDir = transform.forward;
        float forwardSpeed = Vector3.Dot(forwardDir, rb.linearVelocity);

        if (moveInput > 0 && currentSpeedKmh < maxSpeedKmh)
            rb.AddForce(forwardDir * acceleration, ForceMode.Acceleration);
        else if (moveInput < 0)
        {
            if (forwardSpeed > 1f) rb.AddForce(-forwardDir * acceleration * 1.5f, ForceMode.Acceleration);
            else rb.AddForce(-forwardDir * reverseForce, ForceMode.Acceleration);
        }
        else if (moveInput == 0)
            rb.AddForce(-rb.linearVelocity.normalized * autoBrakeForce, ForceMode.Acceleration);
    }

    void HandleSteering()
    {
        float forwardSpeed = Vector3.Dot(transform.forward, rb.linearVelocity);
        if (Mathf.Abs(forwardSpeed) < 0.5f) return;

        float directionMultiplier = Mathf.Sign(forwardSpeed);
        float speedMultiplier = turnCurve != null && turnCurve.length > 0 ? turnCurve.Evaluate(currentSpeedKmh / maxSpeedKmh) : 1f;
        rb.AddRelativeTorque(Vector3.up * steerInput * turnStrength * directionMultiplier * speedMultiplier, ForceMode.Acceleration);
    }

    void HandleTraction()
    {
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        float currentGrip = isDrifting ? driftGrip : normalGrip;
        Vector3 antiDriftForce = -transform.right * localVelocity.x * currentGrip * (rb.mass * 2f);
        rb.AddForce(antiDriftForce, ForceMode.Force);
    }

    // UI Giữ nguyên phong cách của cậu để tiện Debug
    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.fontStyle = FontStyle.Bold;

        string state = groundedWheels > 0 ? (isDrifting ? "ĐANG DRIFT!" : "BÁM ĐƯỜNG") : "ĐANG BAY!";
        style.normal.textColor = isDrifting ? Color.yellow : (groundedWheels == 0 ? Color.red : Color.cyan);

        GUI.Label(new Rect(20, 20, 400, 30), $"TỐC ĐỘ: {currentSpeedKmh:F1} km/h", style);
        GUI.Label(new Rect(20, 50, 400, 30), $"TRẠNG THÁI: {state}", style);
        GUI.Label(new Rect(20, 80, 400, 30), $"SỐ BÁNH CHẠM ĐẤT: {groundedWheels}/4", style);
    }

    void UpdateWheelVisuals()
    {
        // 1. Tính toán lượng góc lăn dựa trên vận tốc
        float forwardSpeed = Vector3.Dot(transform.forward, rb.linearVelocity);
        // (Vận tốc * 360 độ) / Chu vi bánh xe (Pi * đường kính)
        float wheelSpinAmount = (forwardSpeed * 360f / (Mathf.PI * (wheelRadius * 2))) * Time.deltaTime;

        // Cộng dồn vào biến lưu trữ (Dùng % 360 để biến không bị to vô hạn gây tràn bộ nhớ)
        currentWheelSpin = (currentWheelSpin + wheelSpinAmount) % 360f;

        for (int i = 0; i < wheelMeshes.Length; i++)
        {
            // 2. Cập nhật vị trí (Nhún theo Raycast)
            Vector3 rayStartPoint = rayPoints[i].position + (transform.up * rayOriginOffset);
            float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

            RaycastHit hit;
            if (Physics.Raycast(rayStartPoint, -transform.up, out hit, maxRayLength, drivableLayer))
            {
                wheelMeshes[i].position = hit.point + (transform.up * wheelRadius);
            }
            else
            {
                wheelMeshes[i].position = rayPoints[i].position - (transform.up * suspensionRestDist);
            }

            // 3. Cập nhật góc Xoay (GỘP CHUNG VÀO 1 LỆNH QUATERNION DUY NHẤT)
            // Nếu là bánh số 0 và 1 (Bánh trước), áp dụng góc bẻ lái. Bánh sau giữ góc 0.
            float targetSteerAngle = (i == 0 || i == 1) ? (steerInput * maxWheelTurnAngle) : 0f;

            // Áp dụng Toán học tuyệt đối: Trục X (Lăn), Trục Y (Rẽ), Trục Z (0)
            wheelMeshes[i].localRotation = Quaternion.Euler(currentWheelSpin, targetSteerAngle, 0f);
        }
    }

    // Vẽ mắt thần mới (Sẽ thấy điểm bắt đầu tia Raycast cao hơn)
    void OnDrawGizmos()
    {
        if (rayPoints == null || rayPoints.Length == 0) return;

        for (int i = 0; i < rayPoints.Length; i++)
        {
            if (rayPoints[i] != null)
            {
                Vector3 rayStartPoint = rayPoints[i].position + (transform.up * rayOriginOffset);
                float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

                Gizmos.color = Color.red;
                Gizmos.DrawLine(rayStartPoint, rayStartPoint - transform.up * maxRayLength);

                RaycastHit hit;
                if (Physics.Raycast(rayStartPoint, -transform.up, out hit, maxRayLength, drivableLayer))
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawSphere(hit.point, 0.05f);
                    Gizmos.DrawLine(rayStartPoint, hit.point);
                }
            }
        }
    }

    void UpdateChassisVisuals()
    {
        if (chassisMesh == null) return;

        float speedRatio = Mathf.Clamp01(currentSpeedKmh / maxSpeedKmh);

        // ==========================================
        // 1. XỬ LÝ QUÁN TÍNH NGHIÊNG HAI BÊN (ROLL)
        // ==========================================
        if (steerInput != 0)
        {
            // Nếu đang giữ nút rẽ: Tăng dần visualSteerInput về phía steerInput (1 hoặc -1)
            visualSteerInput = Mathf.MoveTowards(visualSteerInput, steerInput, rollBuildUpSpeed * Time.deltaTime);
        }
        else
        {
            // Nếu nhả nút rẽ: Phục hồi thăng bằng nhanh gấp đôi để xe không bị trôi lề mề
            visualSteerInput = Mathf.MoveTowards(visualSteerInput, 0f, (rollBuildUpSpeed * 2f) * Time.deltaTime);
        }

        // Tính góc Roll dựa trên input hình ảnh (đã được làm chậm) thay vì input thật
        float targetRoll = -visualSteerInput * maxRollAngle * speedRatio;


        // ==========================================
        // 2. XỬ LÝ QUÁN TÍNH CHÚI/NGÓC (PITCH)
        // ==========================================
        // Bắt sự kiện: Vừa mới đạp ga/phanh (Input thay đổi từ 0 lên 1/-1, hoặc đảo chiều)
        if (moveInput != 0 && moveInput != lastMoveInput)
        {
            pitchInertiaMultiplier = 1f; // Bơm 100% lực giật quán tính ngay lập tức
        }
        else if (moveInput != 0)
        {
            // Nếu vẫn đang giữ ga/phanh: Từ từ giảm lực giật về mức duy trì (20%)
            pitchInertiaMultiplier = Mathf.Lerp(pitchInertiaMultiplier, sustainedPitchRatio, pitchDecaySpeed * Time.deltaTime);
        }
        else
        {
            // Nhả phím: Trả về 0 nhanh chóng
            pitchInertiaMultiplier = Mathf.Lerp(pitchInertiaMultiplier, 0f, (pitchDecaySpeed * 2f) * Time.deltaTime);
        }

        // Lưu lại input hiện tại để frame sau so sánh
        lastMoveInput = moveInput;

        // Tính góc Pitch: Input * Max Angle * (Hệ số quán tính đang giảm dần)
        // (Thêm check tốc độ để không bị chúi mũi khi nhấn ga lúc dính chặt vào tường)
        float targetPitch = -moveInput * maxPitchAngle * pitchInertiaMultiplier * (currentSpeedKmh > 1f ? 1f : 0f);


        // ==========================================
        // 3. NỘI SUY MƯỢT MÀ VÀ ÁP DỤNG
        // ==========================================
        // Vẫn dùng Lerp cuối cùng để đảm bảo không có bất kỳ sự giật cục (snapping) nào về khung hình
        currentRoll = Mathf.Lerp(currentRoll, targetRoll, chassisSmoothSpeed * Time.deltaTime);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, chassisSmoothSpeed * Time.deltaTime);

        // Xoay vỏ xe theo tọa độ Local
        chassisMesh.localRotation = Quaternion.Euler(currentPitch, 0f, currentRoll);
    }

    void HandleSkidMarks()
    {
        if (skidEmitters == null || skidEmitters.Length == 0) return;

        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        // Chỉ tính trượt khi xe đang di chuyển thực sự (> 2km/h) để tránh lỗi đứng yên
        bool carIsSlipping = (currentSpeedKmh > 2f) && (isDrifting || Mathf.Abs(localVelocity.x) > skidThreshold);

        for (int i = 0; i < skidEmitters.Length; i++)
        {
            var emitter = skidEmitters[i];
            if (emitter.rayPoint == null || emitter.trailRenderer == null) continue;

            Vector3 rayStartPoint = emitter.rayPoint.position + (transform.up * rayOriginOffset);
            float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

            RaycastHit hit;
            if (Physics.Raycast(rayStartPoint, -transform.up, out hit, maxRayLength, drivableLayer))
            {
                if (carIsSlipping)
                {
                    // 1. Dời tọa độ tới điểm chạm đất
                    emitter.trailRenderer.transform.position = hit.point + (hit.normal * roadOffset);

                    // 2. PHÉP THUẬT NẰM Ở ĐÂY:
                    // Trục Z (Forward) ép phải trỏ thẳng lên trời (hit.normal) -> Mặt phẳng sẽ nằm BẸP DÍ xuống đường!
                    // Trục Y (Up) ép hướng theo thân xe (transform.forward)
                    emitter.trailRenderer.transform.rotation = Quaternion.LookRotation(hit.normal, transform.forward);

                    // Bật vẽ
                    emitter.trailRenderer.emitting = true;
                }
                else
                {
                    // Khi không drift: Ngắt vẽ ngay lập tức
                    emitter.trailRenderer.emitting = false;
                }
            }
            else
            {
                // Bánh trên không: Ngắt vẽ
                emitter.trailRenderer.emitting = false;
            }
        }
    }

}