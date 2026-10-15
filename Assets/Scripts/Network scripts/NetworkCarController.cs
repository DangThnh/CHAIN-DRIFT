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

    // Biến theo dõi xem thân xe có đang cạ xuống đất không
    private bool isBodyTouchingGround = false;
    private float bodyTouchResetTimer = 0f;

    private float currentWheelSpin = 0f;
    private float currentRoll = 0f;
    private float currentPitch = 0f;
    private float visualSteerInput = 0f;
    private float pitchInertiaMultiplier = 0f;
    private float lastMoveInput = 0f;

    [Header("Power-Ups: ENHANCE (Juggernaut)")]
    public GameObject armorMesh;        // Kéo cái Armor_Mesh vừa tạo vào đây
    public float armorDuration = 8f;    // Thời gian tác dụng (8 giây)
    public float armoredMassMultiplier = 5f; // Gấp 5 lần trọng lượng
    public Material armorMaterial;

    // Biến mạng lưu trạng thái xem xe có đang Cường Hóa không
    [Networked] public NetworkBool IsArmored { get; set; }

    private float armorTimer = 0f;
    private float originalMass;

    [Header("Anti-Rollover")]
    public float flipDelay = 2.0f;     // Lật ngửa 2 giây sẽ tự cứu
    public float flipForce = 15f;      // Lực búng xe lên không trung để lật lại
    private float rolloverTimer = 0f;

    public enum PlayerTeam { Defender, Hunter }

    [Header("PvP Settings")]
    [Networked] public PlayerTeam MyTeam { get; set; } // Lưu phe của xe
    [Networked] public NetworkBool IsTowingCargo { get; set; } // Đang kéo xe hàng?

    public MeshRenderer carBodyRenderer;
    public Material defenderMaterial; // Màu Xanh
    public Material hunterMaterial;   // Màu Đỏ 

    public Material normalMaterial;   // Màu gốc (không cường hóa)

    [Header("Health & Combat")]
    public float maxHealth = 100f;
    [Networked] public float CurrentHealth { get; set; }
    [Networked] public NetworkBool IsDead { get; set; }

    public float respawnDelay = 2.0f;
    private float respawnTimer = 0f;

    // Biến lưu trữ Material gốc để làm hiệu ứng nhấp nháy đỏ
    private Material currentBlinkColor;

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody>();
        originalMass = rb.mass; // LƯU LẠI KHỐI LƯỢNG GỐC

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

        if (Object.HasStateAuthority)
        {
            CurrentHealth = maxHealth;
            IsDead = false;
        }
    }

    public override void FixedUpdateNetwork()
    {

        // === NẾU XE CHẾT: TẮT NÚT BẤM, ĐẾM NGƯỢC HỒI SINH ===
        if (IsDead)
        {
            if (Object.HasStateAuthority)
            {
                respawnTimer -= Runner.DeltaTime;
                if (respawnTimer <= 0) Respawn();
            }
            return; // NGẮT HÀM TẠI ĐÂY! Không chạy raycast, không lái xe!
        }

        bodyTouchResetTimer -= Runner.DeltaTime;
        if (bodyTouchResetTimer <= 0) isBodyTouchingGround = false;

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

        if (Object.HasStateAuthority)
        {
            if (IsArmored)
            {
                armorTimer -= Runner.DeltaTime;
                if (armorTimer <= 0)
                {
                    IsArmored = false;
                    rb.mass = originalMass; // Trả lại 1000kg
                    Debug.Log(">> SERVER: XE ĐÃ HẾT CƯỜNG HÓA! <<");
                }
            }
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

        // ========================================================
        // HỆ THỐNG CHỐNG LẬT (CHUẨN VẬT LÝ CỦA CẬU)
        // ========================================================
        float rollAngle = transform.eulerAngles.z;
        float pitchAngle = transform.eulerAngles.x;

        if (rollAngle > 180) rollAngle -= 360;
        if (pitchAngle > 180) pitchAngle -= 360;

        bool isUpsideDown = Mathf.Abs(rollAngle) > 70f || Mathf.Abs(pitchAngle) > 70f;

        // ĐIỀU KIỆN KÍCH HOẠT VÀNG:
        // 1. Xe bị nghiêng ngửa
        // 2. Không có bánh nào chạm đất (để tránh chống lật khi đang leo vách núi nghiêng)
        // 3. THÂN XE ĐANG NẰM CẠ XUỐNG MẶT ĐẤT (isBodyTouchingGround = true)

        if (isUpsideDown && groundedWheels == 0 && isBodyTouchingGround)
        {
            rolloverTimer += Runner.DeltaTime;

            if (rolloverTimer >= flipDelay)
            {
                // BÚNG XE LÊN TRỜI VÀ LẬT LẠI!
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                transform.position += Vector3.up * 2.5f; // Búng cao lên chút để không vướng đất
                transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);

                rolloverTimer = 0f;
                Debug.Log(">> LẬT XE THÀNH CÔNG NHỜ CHẠM ĐẤT! <<");
            }
        }
        else
        {
            // Nếu xe đang bay trên không, hoặc đã đứng thẳng, reset bộ đếm!
            rolloverTimer = 0f;
        }
    }

    public override void Render()
    {
        // NetworkRigidbody tự động lo việc làm mượt vị trí xe thông qua cục Interpolation Target.

        // 1. NẾU CHẾT: GIẤU HOÀN TOÀN CỤC VISUALS
        if (chassisMesh != null)
        {
            // Lấy cục Gốc của Visuals (Chứa cả vỏ lẫn bánh xe)
            GameObject visualsRoot = chassisMesh.parent.gameObject;
            visualsRoot.SetActive(!IsDead);
        }

        // Nếu xe chết thì bỏ qua vẽ bánh xe và vệt lốp
        if (IsDead) return;

        if (armorMesh != null)
        {
            if (armorMesh.activeSelf != IsArmored)
            {
                armorMesh.SetActive(IsArmored);
            }
        }

        // 2. LOGIC MÀU SƠN VÀ NHẤP NHÁY ĐỎ KHI SẮP CHẾT (< 30% MÁU)
        if (carBodyRenderer != null)
        {
            Material targetMat = normalMaterial;
            if (IsArmored) targetMat = armorMaterial;
            else if (MyTeam == PlayerTeam.Defender) targetMat = defenderMaterial;
            else if (MyTeam == PlayerTeam.Hunter) targetMat = hunterMaterial;

            // Hiệu ứng nhấp nháy đỏ khẩn cấp (Dùng hàm Lerp màu)
            if (CurrentHealth > 0 && CurrentHealth <= (maxHealth * 0.3f))
            {
                // Chớp tắt liên tục giữa màu gốc và Màu Đỏ
                float blink = Mathf.PingPong(Time.time * 5f, 1f);
                carBodyRenderer.material.color = Color.Lerp(targetMat.color, Color.red, blink);
            }
            else
            {
                // Khôi phục material gốc nếu máu an toàn
                if (carBodyRenderer.material != targetMat) carBodyRenderer.material = targetMat;
            }
        }

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


    // Hàm này chạy trên Server khi xe ăn được hộp
    public void ActivateArmor()
    {
        if (!Object.HasStateAuthority) return;

        IsArmored = true;
        armorTimer = armorDuration;
        rb.mass = originalMass * armoredMassMultiplier; // Bơm trọng lượng lên 5000kg!

        Debug.Log(">> SERVER: XE ĐÃ ĐƯỢC CƯỜNG HÓA! TRỌNG LƯỢNG X5! <<");
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!Object.HasStateAuthority) return;

        if (IsArmored)
        {
            // 1. Logic húc tường vụn (Giữ nguyên)
            DestructibleObject wall = collision.gameObject.GetComponent<DestructibleObject>();
            if (wall != null)
            {
                rb.AddForce(transform.forward * (acceleration * 2f), ForceMode.Impulse);
            }

            // ==========================================
            // 2. LOGIC HÚC KHỐI HÀNG (ĐÃ CẬP NHẬT FRIENDLY FIRE)
            // ==========================================
            CargoManager cargo = collision.gameObject.GetComponentInParent<CargoManager>();
            if (cargo != null)
            {
                float impactForce = collision.relativeVelocity.magnitude;

                if (impactForce > 10f)
                {
                    // KIỂM TRA THẺ CĂN CƯỚC:
                    // CHỈ CÓ PHE PHÁ ĐÁM (HUNTER) MỚI ĐƯỢC PHÉP RÚT MÁU KHỐI HÀNG!
                    if (MyTeam == PlayerTeam.Hunter)
                    {
                        float damage = impactForce * 10f;
                        cargo.RPC_TakeDamage(damage);
                    }
                    else
                    {
                        // Nếu là Defender húc vào: Không trừ máu, chỉ báo Debug
                        Debug.Log(">> PHE BẢO VỆ VÔ TÌNH HÚC VÀO HÀNG! (Không mất máu) <<");
                    }

                    // Tạo lực nảy dội ra để xe (dù là phe nào) cũng không dính chặt vào Cargo
                    rb.AddForce(-transform.forward * 5000f, ForceMode.Impulse);
                }
            }
            // ==========================================
            // LOGIC HÚC XE ĐỐI PHƯƠNG
            // ==========================================
            NetworkCarController enemyCar = collision.gameObject.GetComponentInParent<NetworkCarController>();
            if (enemyCar != null)
            {
                // CHỈ TRỪ MÁU NẾU KHÁC PHE (Chống Friendly Fire)
                if (enemyCar.MyTeam != this.MyTeam)
                {
                    float impactForce = collision.relativeVelocity.magnitude;
                    if (impactForce > 10f)
                    {
                        // Gây sát thương nặng (Tốc độ x 2)
                        enemyCar.RPC_TakeDamage(impactForce * 2f);

                        // Lực đẩy xe tăng xé gió
                        rb.AddForce(transform.forward * (acceleration * 2f), ForceMode.Impulse);
                        Debug.Log(">> BỌC THÉP HÚC VĂNG KẺ THÙ! <<");
                    }
                }
            }
        }
    }

    // Hàm này chạy liên tục chừng nào xe còn đang cạ vào vật khác
    void OnCollisionStay(Collision collision)
    {
        // Nếu cái thứ đang cạ vào có thuộc Layer mặt đường (DrivableLayer)
        if (((1 << collision.gameObject.layer) & drivableLayer) != 0)
        {
            isBodyTouchingGround = true;
            bodyTouchResetTimer = 0.1f; // Gia hạn thời gian chạm đất
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(float damage)
    {
        if (IsDead) return;

        CurrentHealth -= damage;
        Debug.Log($"[{MyTeam}] Xe bị húc! Máu còn: {CurrentHealth}");

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }
    }

    void Die()
    {
        IsDead = true;
        respawnTimer = respawnDelay;

        // NẾU ĐANG KÉO HÀNG MÀ CHẾT -> ÉP ĐỨT DÂY XÍCH NGAY LẬP TỨC!
        NetworkTowController tow = GetComponent<NetworkTowController>();
        if (tow != null) tow.RPC_ForceDetach(); // (Lát nữa ta sẽ viết hàm này bên TowController)

        Debug.Log($"[{MyTeam}] XE ĐÃ NỔ TUNG! Chờ hồi sinh...");
    }

    void Respawn()
    {
        IsDead = false;
        CurrentHealth = maxHealth;

        // TÌM XE HÀNG ĐỂ TÍNH TOÁN VỊ TRÍ HỒI SINH
        CargoManager cargo = FindObjectOfType<CargoManager>();
        if (cargo != null)
        {
            // Lấy vị trí cách đuôi xe hàng 100 mét
            Vector3 spawnPos = cargo.transform.position - (cargo.transform.forward * 100f);
            spawnPos.y += 3f; // Thả từ trên cao 3m xuống cho an toàn khỏi kẹt đất

            // Dịch chuyển xe
            rb.position = spawnPos;
            rb.rotation = cargo.transform.rotation;
        }
        else
        {
            rb.position = new Vector3(0, 3, 0); // Dự phòng nếu ko thấy khối hàng
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Debug.Log($"[{MyTeam}] ĐÃ HỒI SINH CÁCH XE HÀNG 100M!");
    }
}