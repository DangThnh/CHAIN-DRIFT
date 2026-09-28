using UnityEngine;
using Fusion;

public class NetworkCarController : NetworkBehaviour
{
    // ... [GIỮ NGUYÊN TOÀN BỘ PHẦN KHAI BÁO BIẾN Ở HEADER NHƯ CŨ] ...
    [Header("References")]
    public Transform[] rayPoints;
    public Transform[] wheelMeshes;

    // Lưu tọa độ mặc định của 4 bánh xe trong hốc bánh
    private Vector3[] defaultWheelLocalPositions;

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
    public float rayOriginOffset = 0.5f;
    public float wheelRadius = 0.3f;

    [Header("Steering")]
    public float turnStrength = 1500f;
    public float maxWheelTurnAngle = 35f;
    public AnimationCurve turnCurve;

    [Header("Traction & Drift")]
    [Range(0f, 1f)] public float normalGrip = 0.95f;
    [Range(0f, 1f)] public float driftGrip = 0.2f;

    [Header("Chassis Visuals (Body Roll & Pitch)")]
    public Transform chassisMesh;
    public float maxRollAngle = 10f;
    public float maxPitchAngle = 5f;
    public float chassisSmoothSpeed = 8f;
    public float rollBuildUpSpeed = 2.5f;
    public float pitchDecaySpeed = 3f;
    public float sustainedPitchRatio = 0.2f;

    [Header("Skid Marks (Chuyên nghiệp)")]
    public float skidThreshold = 2.5f;
    private int[] lastSkidmarkIndices;

    // Thêm 2 biến nội bộ này để làm mượt
    [Header("Network Smoothing")]
    public float clientInterpolationSpeed = 15f;

    private Rigidbody rb;
    private float moveInput;
    private float steerInput;
    private bool isDrifting;
    private int groundedWheels;
    private float currentSpeedKmh;

    private float currentWheelSpin = 0f;
    private float currentRoll = 0f;
    private float currentPitch = 0f;
    private float visualSteerInput = 0f;
    private float pitchInertiaMultiplier = 0f;
    private float lastMoveInput = 0f;




    public override void Spawned()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.5f, 0);

        if (HasInputAuthority)
        {
            CarCameraController cam = FindObjectOfType<CarCameraController>();
            if (cam != null) cam.carTarget = transform;
        }

        // ==========================================================
        // ĐÃ XÓA ĐOẠN CODE BẮT CLIENT TẮT KINEMATIC Ở ĐÂY!
        // Bây giờ cả Host và Client đều là vật lý thật (isKinematic = false)
        // ==========================================================
        rb.isKinematic = false;

        if (rayPoints != null && rayPoints.Length >= 4)
        {
            lastSkidmarkIndices = new int[2] { -1, -1 };
        }

        // LƯU LẠI VỊ TRÍ GỐC CỦA 4 BÁNH XE TRONG CỤC VISUALS
        if (wheelMeshes != null && wheelMeshes.Length > 0)
        {
            defaultWheelLocalPositions = new Vector3[wheelMeshes.Length];
            for (int i = 0; i < wheelMeshes.Length; i++)
            {
                if (wheelMeshes[i] != null)
                {
                    defaultWheelLocalPositions[i] = wheelMeshes[i].localPosition;
                }
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 1. ĐỌC DỮ LIỆU INPUT
        // Trên Host: GetInput lấy dữ liệu của Client gửi lên.
        // Trên Client: GetInput lấy dữ liệu phím cục bộ để chạy Client-Side Prediction!
        if (GetInput(out NetworkInputData data))
        {
            moveInput = data.moveInput;
            steerInput = data.steerInput;
            isDrifting = data.isDrifting;
        }
        else
        {
            moveInput = 0; steerInput = 0; isDrifting = false;
        }

        // 2. TÍNH TOÁN VẬT LÝ (Chạy đồng thời trên cả Host và Client để đồng bộ dự đoán)
        currentSpeedKmh = rb.linearVelocity.magnitude * 3.6f;
        groundedWheels = 0;

        for (int i = 0; i < rayPoints.Length; i++)
        {
            // Bắn tia từ điểm gắn phuộc nhún
            Vector3 rayStartPoint = rayPoints[i].position + (transform.up * rayOriginOffset);
            float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

            RaycastHit hit;
            // Dùng LayerMask chuẩn mà cậu đã set
            if (Physics.Raycast(rayStartPoint, -transform.up, out hit, maxRayLength, drivableLayer))
            {
                groundedWheels++;
                float actualDist = hit.distance - rayOriginOffset;
                float offset = suspensionRestDist - actualDist;

                // Lấy vận tốc tại điểm tiếp xúc bánh xe
                float velAtPoint = Vector3.Dot(transform.up, rb.GetPointVelocity(rayPoints[i].position));
                float force = (offset * springStrength) - (velAtPoint * springDamper);

                // Áp dụng lực lò xo đẩy thân xe
                rb.AddForceAtPosition(transform.up * force, rayPoints[i].position);
            }
        }

        // Chỉ tác động lực lái khi xe đã tiếp đất
        if (groundedWheels > 0)
        {
            HandleMovement();
            HandleSteering();
            HandleTraction();
        }
    }

    public override void Render()
    {
        // NetworkRigidbody tự động lo việc làm mượt vị trí xe thông qua cục Interpolation Target.
        // Ở đây chúng ta CHỈ lo phần hiệu ứng hình ảnh:
        UpdateWheelVisuals();
        UpdateChassisVisuals();
        HandleSkidMarks();

        // BẢO HIỂM CAMERA: Đảm bảo Camera luôn tìm thấy xe chính chủ
        CheckAndAssignCamera();
    }

    // ... [GIỮ NGUYÊN TẤT CẢ CÁC HÀM CÒN LẠI: HandleMovement, HandleSteering, HandleTraction, UpdateWheelVisuals, UpdateChassisVisuals, HandleSkidMarks] ...

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

    void UpdateWheelVisuals()
    {
        if (wheelMeshes == null || defaultWheelLocalPositions == null) return;

        // 1. Tính toán góc quay lăn bánh xe
        float forwardSpeed = Vector3.Dot(transform.forward, rb.linearVelocity);
        float wheelSpinAmount = (forwardSpeed * 360f / (Mathf.PI * (wheelRadius * 2))) * Time.deltaTime;
        currentWheelSpin = (currentWheelSpin + wheelSpinAmount) % 360f;

        for (int i = 0; i < wheelMeshes.Length; i++)
        {
            if (wheelMeshes[i] == null) continue;

            // Bắn tia raycast đo khoảng cách
            Vector3 rayStartPoint = rayPoints[i].position + (transform.up * rayOriginOffset);
            float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

            float targetLocalY = defaultWheelLocalPositions[i].y;

            RaycastHit hit;
            if (Physics.Raycast(rayStartPoint, -transform.up, out hit, maxRayLength, drivableLayer))
            {
                // Tính độ nén lò xo
                float actualDist = hit.distance - rayOriginOffset;
                float compression = suspensionRestDist - actualDist;

                // Nâng/Hạ bánh xe theo trục Y địa phương
                targetLocalY = defaultWheelLocalPositions[i].y + compression;
            }
            else
            {
                // Khi xe bay lên không trung: Bánh xệ xuống một chút
                targetLocalY = defaultWheelLocalPositions[i].y - (suspensionRestDist * 0.3f);
            }

            // ====================================================================
            // PHÉP THUẬT NẰM Ở ĐÂY:
            // Khóa chặt X và Z theo cục Visuals (Mượt 144Hz)!
            // Chỉ Lerp làm mượt trục Y (độ nhún) để bánh xe êm ái tuyệt đối!
            // ====================================================================
            Vector3 targetLocalPos = new Vector3(defaultWheelLocalPositions[i].x, targetLocalY, defaultWheelLocalPositions[i].z);
            wheelMeshes[i].localPosition = Vector3.Lerp(wheelMeshes[i].localPosition, targetLocalPos, 25f * Time.deltaTime);

            // Xoay bánh xe (Lăn và Rẽ)
            float targetSteerAngle = (i == 0 || i == 1) ? (steerInput * maxWheelTurnAngle) : 0f;
            wheelMeshes[i].localRotation = Quaternion.Euler(currentWheelSpin, targetSteerAngle, 0f);
        }
    }

    void UpdateChassisVisuals()
    {
        if (chassisMesh == null) return;
        float speedRatio = Mathf.Clamp01(currentSpeedKmh / maxSpeedKmh);

        if (steerInput != 0)
            visualSteerInput = Mathf.MoveTowards(visualSteerInput, steerInput, rollBuildUpSpeed * Time.deltaTime);
        else
            visualSteerInput = Mathf.MoveTowards(visualSteerInput, 0f, (rollBuildUpSpeed * 2f) * Time.deltaTime);

        float targetRoll = -visualSteerInput * maxRollAngle * speedRatio;

        if (moveInput != 0 && moveInput != lastMoveInput)
            pitchInertiaMultiplier = 1f;
        else if (moveInput != 0)
            pitchInertiaMultiplier = Mathf.Lerp(pitchInertiaMultiplier, sustainedPitchRatio, pitchDecaySpeed * Time.deltaTime);
        else
            pitchInertiaMultiplier = Mathf.Lerp(pitchInertiaMultiplier, 0f, (pitchDecaySpeed * 2f) * Time.deltaTime);

        lastMoveInput = moveInput;
        float targetPitch = -moveInput * maxPitchAngle * pitchInertiaMultiplier * (currentSpeedKmh > 1f ? 1f : 0f);

        currentRoll = Mathf.Lerp(currentRoll, targetRoll, chassisSmoothSpeed * Time.deltaTime);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, chassisSmoothSpeed * Time.deltaTime);

        chassisMesh.localRotation = Quaternion.Euler(currentPitch, 0f, currentRoll);
    }

    void HandleSkidMarks()
    {
        // Kiểm tra an toàn: Phải có SkidmarkManager và đủ 4 phuộc nhún
        if (SkidmarkManager.Instance == null || rayPoints == null || rayPoints.Length < 4) return;

        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        bool carIsSlipping = (currentSpeedKmh > 2f) && (isDrifting || Mathf.Abs(localVelocity.x) > skidThreshold);

        // Danh sách Index của 2 bánh sau: [2] là Sau Trái (RL), [3] là Sau Phải (RR)
        int[] rearIndices = new int[] { 2, 3 };

        for (int i = 0; i < rearIndices.Length; i++)
        {
            int wheelIndex = rearIndices[i];
            Transform rayPoint = rayPoints[wheelIndex];
            if (rayPoint == null) continue;

            // Bắn tia từ đúng phuộc nhún bánh sau xuống đất
            Vector3 rayStart = rayPoint.position + (transform.up * rayOriginOffset);
            float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

            RaycastHit hit;
            if (Physics.Raycast(rayStart, -transform.up, out hit, maxRayLength, drivableLayer))
            {
                if (carIsSlipping)
                {
                    float intensity = Mathf.Clamp01(Mathf.Abs(localVelocity.x) / 10f);
                    if (isDrifting) intensity = Mathf.Max(intensity, 0.8f);

                    // ĐÓNG DẤU CHÍNH XÁC TẠI ĐIỂM TIẾP XÚC CỦA BÁNH SAU
                    lastSkidmarkIndices[i] = SkidmarkManager.Instance.AddSkidMark(hit.point, hit.normal, intensity, lastSkidmarkIndices[i]);
                }
                else
                {
                    lastSkidmarkIndices[i] = -1;
                }
            }
            else
            {
                lastSkidmarkIndices[i] = -1;
            }
        }
    }

    // VẼ TIA RAYCAST RA MÀN HÌNH SCENE ĐỂ DỄ SETUP
    void OnDrawGizmos()
    {
        if (rayPoints == null || rayPoints.Length == 0) return;

        for (int i = 0; i < rayPoints.Length; i++)
        {
            if (rayPoints[i] != null)
            {
                // Điểm bắt đầu của tia (Nâng lên theo offset)
                Vector3 rayStartPoint = rayPoints[i].position + (transform.up * rayOriginOffset);
                float maxRayLength = suspensionRestDist + rayOriginOffset + 0.2f;

                Gizmos.color = Color.red;
                // Vẽ đường thẳng màu đỏ thể hiện hướng và độ dài của tia
                Gizmos.DrawLine(rayStartPoint, rayStartPoint - transform.up * maxRayLength);

                RaycastHit hit;
                // Nếu tia chạm trúng một thứ gì đó (mọi layer)
                if (Physics.Raycast(rayStartPoint, -transform.up, out hit, maxRayLength, -1))
                {
                    Gizmos.color = Color.green;
                    // Vẽ cục bi màu xanh lá cây tại điểm chạm
                    Gizmos.DrawSphere(hit.point, 0.05f);
                    Gizmos.DrawLine(rayStartPoint, hit.point);
                }
            }
        }
    }

    private bool cameraLocked = false;

    void CheckAndAssignCamera()
    {
        if (HasInputAuthority && !cameraLocked)
        {
            CarCameraController cam = FindObjectOfType<CarCameraController>();
            if (cam != null)
            {
                // ========================================================
                // THAY ĐỔI CỰC KỲ QUAN TRỌNG:
                // Không bám theo 'this.transform' (Cục gốc 60Hz) nữa!
                // BẮT CAMERA PHẢI BÁM THEO CỤC 'chassisMesh' HOẶC CỤC 'Visuals' (Đã được nội suy 144Hz)!
                // ========================================================
                Transform smoothTarget = chassisMesh != null ? chassisMesh.parent : this.transform;
                cam.SetTarget(smoothTarget);

                cameraLocked = true;
                Debug.Log("<color=green>CAMERA ĐÃ KHÓA VÀO CỤC NỘI SUY MƯỢT 144HZ!</color>");
            }
        }
    }

}