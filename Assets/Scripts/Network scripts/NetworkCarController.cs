using UnityEngine;
using Fusion;
using System.Collections.Generic;

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

    // --- Bước 0: trạng thái sống/chết & material nhấp nháy ---
    private Collider bodyCollider;
    private RigidbodyConstraints originalConstraints;
    private bool aliveStateApplied = true;
    private Material blinkMat;      // 1 instance duy nhất, tạo lười khi cần
    private Material blinkSource;

    //POWER UP BOC GIAP!!!
    [Header("Power-Ups: ENHANCE (Juggernaut)")]
    public GameObject armorMesh;        // Kéo cái Armor_Mesh vừa tạo vào đây
    public float armorDuration = 8f;    // Thời gian tác dụng (8 giây)
    public float armoredMassMultiplier = 5f; // Gấp 5 lần trọng lượng
    public Material armorMaterial;

    // Biến mạng lưu trạng thái xem xe có đang Cường Hóa không
    [Networked] public NetworkBool IsArmored { get; set; }

    private float armorTimer = 0f;
    private float originalMass;

    //POWER UP SUNG!!!
    [Header("Power-Ups: GUNS")]
    public GameObject crosshairUI; // Kéo cục Image Crosshair ngoài Canvas vào đây
    public GameObject gunTurretObj;     // Kéo model 3D khẩu súng vào đây
    public Transform gunFirePoint;      // Điểm đầu nòng súng để bắn tia Hitscan ra
    public float gunDuration = 10f;     // Súng tồn tại 10s
    public float fireRate = 0.2f;       // Tốc độ xả đạn (0.2s/viên)
    public float gunDamage = 15f;       // Sát thương mỗi viên
    public LineRenderer bulletTracer;   // Kéo 1 LineRenderer vào đây để làm tia đạn
    public float tracerDisplayTime = 0.05f; // Đạn chỉ chớp 0.05s rồi tắt
    private float tracerTimer = 0f;

    [Tooltip("Góc bù trừ nếu model 3D bị ngược hướng (VD: set Y = 180)")]
    public Vector3 gunModelRotationOffset = new Vector3(0, 180f, 0);

    //POWER UP KHOAN!!!
    [Header("Power-Ups: DRILL")]
    public GameObject drillMesh;                       // Mesh mũi khoan (KHÔNG có Collider), để tắt sẵn
    public float drillDuration = 10f;
    public float drillDps = 35f;                       // Sát thương mỗi giây khi chạm
    public float drillTickInterval = 0.2f;             // Chu kỳ quét va chạm (server)
    public float drillRadius = 1.0f;                   // Bán kính vùng chạm ở đầu xe
    public Vector3 drillTipLocalOffset = new Vector3(0f, 0.5f, 2.2f); // Theo toạ độ local của root xe
    public float drillSpinSpeed = 720f;                // Độ/giây, chỉ là hiệu ứng

    [Networked] public NetworkBool IsDrilling { get; set; }
    [Networked] private TickTimer DrillTimer { get; set; }

    private float drillTickAccum = 0f;
    private readonly Collider[] drillHits = new Collider[16];
    private readonly HashSet<Component> drillAlreadyHit = new HashSet<Component>();

    //POWER UP TÊN LỬA!!!
    [Header("Power-Ups: MISSILE")]
    public NetworkPrefabRef missilePrefab;
    public GameObject missileLauncherMesh;   // tùy chọn, KHÔNG có Collider, tắt sẵn
    public GameObject missileReticle;        // đĩa phẳng, KHÔNG có Collider, tắt sẵn
    public float missileTargetAhead = 50f;   // hồng tâm cách xe bao nhiêu mét dọc đường
    public Vector3 missileLaunchLocalOffset = new Vector3(0f, 1.8f, 0f);

    [Networked] public NetworkBool HasMissile { get; set; }
    private bool fireInput, lastFireInput;
    private int trackHint;


    // BIẾN MẠNG: Lưu vị trí mà đầu nòng súng đang nhắm tới để mọi máy cùng thấy
    [Networked] public Vector3 NetGunLookTarget { get; set; }
    [Networked] public NetworkBool IsArmed { get; set; }
    private float gunTimer = 0f;
    private float nextFireTime = 0f;

    // Biến để ngắm chuột
    private Camera mainCam;




    [Header("Anti-Rollover")]
    public float flipDelay = 2.0f;     // Lật ngửa 2 giây sẽ tự cứu
    public float flipForce = 15f;      // Lực búng xe lên không trung để lật lại
    private float rolloverTimer = 0f;

    [Header("Wall Repel (chống kẹt tường)")]
    public LayerMask wallLayer;            // Chọn layer TrackWall
    public float wallProbeLength = 2.2f;   // Tính từ giữa xe. Nên lớn hơn nửa bề ngang xe khoảng 0.7-1m
    public float wallRepelAccel = 25f;     // Gia tốc đẩy tối đa (m/s²), chỉnh theo cảm giác
    public float wallProbeHeight = 0.4f;   // Độ cao tia so với gốc xe

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

        bodyCollider = GetComponent<Collider>();
        originalConstraints = rb.constraints;

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

        mainCam = Camera.main; // Lấy camera chính để phóng tia ngắm bắn
    }

    public override void FixedUpdateNetwork()
    {
        ApplyAliveState(!IsDead);

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
            fireInput = data.isFirePressed;
        }
        else
        {
            moveInput = 0; steerInput = 0; isDrifting = false; fireInput = false;
        }

        // ==========================================
        // QUẢN LÝ THỜI GIAN POWER-UP (CHỈ HOST LÀM)
        // ==========================================
        if (Object.HasStateAuthority)
        {
            if (IsArmored)
            {
                armorTimer -= Runner.DeltaTime;
                if (armorTimer <= 0) { IsArmored = false; rb.mass = originalMass; }
            }

            if (IsArmed)
            {
                gunTimer -= Runner.DeltaTime;
                if (gunTimer <= 0) { IsArmed = false; }
            }

            if (IsDrilling)
            {
                if (DrillTimer.Expired(Runner)) { IsDrilling = false; DrillTimer = default; }
                else ServerDrillTick();
            }

            if (HasMissile && fireInput && !lastFireInput) ServerFireMissile();
            lastFireInput = fireInput;
        }

        // ==========================================
        // CƠ CHẾ BẮN SÚNG HITSCAN (Chỉ người đang lái xe này mới được bắn)
        // ==========================================
        UpdateAimingTarget();


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
            HandleWallRepel();
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

    //Hệ thống đẩy tường chống kẹt
    void HandleWallRepel()
    {
        if (wallLayer.value == 0) return;
        Vector3 origin = transform.position + transform.up * wallProbeHeight;
        ApplyWallProbe(origin, transform.right);
        ApplyWallProbe(origin, -transform.right);
    }

    void ApplyWallProbe(Vector3 origin, Vector3 dir)
    {
        if (Physics.Raycast(origin, dir, out RaycastHit hit, wallProbeLength, wallLayer, QueryTriggerInteraction.Ignore))
        {
            Vector3 push = hit.normal;
            push.y = 0f;
            if (push.sqrMagnitude < 1e-6f) return;
            push.Normalize();
            float t = 1f - hit.distance / wallProbeLength;   // càng gần tường càng mạnh
            rb.AddForce(push * (wallRepelAccel * t), ForceMode.Acceleration);
        }
    }

    //Hàm vẽ
    public override void Render()
    {
        ApplyAliveState(!IsDead);

        // NetworkRigidbody tự động lo việc làm mượt vị trí xe thông qua cục Interpolation Target.

        // 1. NẾU CHẾT: GIẤU HOÀN TOÀN CỤC VISUALS
        if (chassisMesh != null)
        {
            // Lấy cục Gốc của Visuals (Chứa cả vỏ lẫn bánh xe)
            GameObject visualsRoot = chassisMesh.parent.gameObject;
            visualsRoot.SetActive(!IsDead);
        }

        // tên lửa
        if (missileLauncherMesh != null && missileLauncherMesh.activeSelf != HasMissile)
            missileLauncherMesh.SetActive(HasMissile);

        if (missileReticle != null)
        {
            bool show = HasMissile && HasInputAuthority && !IsDead;
            if (missileReticle.activeSelf != show) missileReticle.SetActive(show);
            if (show && TryGetMissileTarget(out Vector3 tgt))
            {
                missileReticle.transform.position = tgt + Vector3.up * 0.1f;
                missileReticle.transform.rotation = Quaternion.identity;
            }
        }

        // Nếu xe chết thì bỏ qua vẽ bánh xe và vệt lốp
        if (IsDead)
        {
            if (crosshairUI != null) crosshairUI.SetActive(false);
            return;
        }

        if (armorMesh != null)
        {
            if (armorMesh.activeSelf != IsArmored)
            {
                armorMesh.SetActive(IsArmored);
            }
        }

        if (drillMesh != null)
        {
            if (drillMesh.activeSelf != IsDrilling) drillMesh.SetActive(IsDrilling);
            if (IsDrilling) drillMesh.transform.Rotate(0f, drillSpinSpeed * Time.deltaTime, 0f, Space.Self);
        }

        if (gunTurretObj != null)
        {
            bool showGun = IsArmed && !IsDead;
            if (gunTurretObj.activeSelf != showGun) gunTurretObj.SetActive(showGun);

            // TẤT CẢ CÁC MÁY ĐỀU PHẢI XOAY SÚNG THEO BIẾN MẠNG NetGunLookTarget
            if (showGun && NetGunLookTarget != Vector3.zero)
            {
                Vector3 lookDir = NetGunLookTarget - gunTurretObj.transform.position;
                if (lookDir != Vector3.zero)
                {
                    // Luôn luôn nhân thêm góc gunModelRotationOffset trong mỗi frame
                    Quaternion targetRot = Quaternion.LookRotation(lookDir) * Quaternion.Euler(gunModelRotationOffset);
                    gunTurretObj.transform.rotation = Quaternion.Slerp(gunTurretObj.transform.rotation, targetRot, 20f * Time.deltaTime);
                }
            }

            if (crosshairUI != null) crosshairUI.SetActive(showGun && HasInputAuthority);
        }

        // TẮT TIA ĐẠN SAU VÀI MILI GIÂY
        if (bulletTracer != null && bulletTracer.enabled)
        {
            tracerTimer -= Time.deltaTime;
            if (tracerTimer <= 0) bulletTracer.enabled = false;
        }


        // 2. LOGIC MÀU SƠN VÀ NHẤP NHÁY ĐỎ KHI SẮP CHẾT (< 30% MÁU)
        if (carBodyRenderer != null)
        {
            Material targetMat = normalMaterial;
            if (IsArmored) targetMat = armorMaterial;
            else if (MyTeam == PlayerTeam.Defender) targetMat = defenderMaterial;
            else if (MyTeam == PlayerTeam.Hunter) targetMat = hunterMaterial;

            bool lowHealth = CurrentHealth > 0 && CurrentHealth <= (maxHealth * 0.3f);
            if (lowHealth && targetMat != null)
            {
                if (blinkMat == null) blinkMat = new Material(targetMat);
                if (blinkSource != targetMat)
                {
                    blinkMat.CopyPropertiesFromMaterial(targetMat);
                    blinkSource = targetMat;
                }
                float blink = Mathf.PingPong(Time.time * 5f, 1f);
                blinkMat.color = Color.Lerp(targetMat.color, Color.red, blink);
                if (carBodyRenderer.sharedMaterial != blinkMat) carBodyRenderer.sharedMaterial = blinkMat;
            }
            else if (targetMat != null && carBodyRenderer.sharedMaterial != targetMat)
            {
                carBodyRenderer.sharedMaterial = targetMat;
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
    public void GivePowerUp(PowerUpBox.PowerUpType type)
    {
        if (!Object.HasStateAuthority) return;
        ClearAllPowerUps();

        if (type == PowerUpBox.PowerUpType.Armor)
        {
            IsArmored = true;
            armorTimer = armorDuration;
            rb.mass = originalMass * armoredMassMultiplier;
        }
        else if (type == PowerUpBox.PowerUpType.Gun)
        {
            IsArmed = true;
            gunTimer = gunDuration;
        }
        else if (type == PowerUpBox.PowerUpType.Drill)
        {
            IsDrilling = true;
            DrillTimer = TickTimer.CreateFromSeconds(Runner, drillDuration);
            drillTickAccum = 0f;
        }
        else if (type == PowerUpBox.PowerUpType.Missile)
        {
            HasMissile = true;
        }
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

        ClearAllPowerUps();
        ApplyAliveState(false);

        // NẾU ĐANG KÉO HÀNG MÀ CHẾT -> ÉP ĐỨT DÂY XÍCH NGAY LẬP TỨC!
        NetworkTowController tow = GetComponent<NetworkTowController>();
        if (tow != null) tow.RPC_ForceDetach(); // (Lát nữa ta sẽ viết hàm này bên TowController)

        Debug.Log($"[{MyTeam}] XE ĐÃ NỔ TUNG! Chờ hồi sinh...");
    }

    void Respawn()
    {
        IsDead = false;
        CurrentHealth = maxHealth;
        ApplyAliveState(true);

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

    // ====================================================================
    // CƠ CHẾ BẮN SÚNG & ĐỒNG BỘ GÓC NGẮM HOÀN CHỈNH
    // ====================================================================

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SyncAimPoint(Vector3 point)
    {
        NetGunLookTarget = point; // Server nhận và đồng bộ cho tất cả các máy
    }

    // 1. CẬP NHẬT GÓC NGẮM LIÊN TỤC TRONG FIXED UPDATE (KHÔNG CHỜ BẤM BẮN MỚI GỬI)
    void UpdateAimingTarget()
    {
        if (!IsArmed || !HasInputAuthority || mainCam == null) return;

        Ray ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = ray.GetPoint(200f);

        // Bắn tia từ Camera để tìm điểm trúng đạn
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 300f))
        {
            // Bỏ qua nếu tia vô tình chạm vào chính cái xe của người đang bắn
            if (hitInfo.collider.transform.root != this.transform.root)
            {
                targetPoint = hitInfo.point;
            }
        }

        NetGunLookTarget = targetPoint;
        RPC_SyncAimPoint(targetPoint); // Gửi liên tục để máy bạn thấy súng quay mượt mà!

        // Xử lý bắn khi đè chuột trái
        if (Input.GetMouseButton(0) && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            FireHitscanWeapon(targetPoint);
        }
    }

    void FireHitscanWeapon(Vector3 targetPoint)
    {
        Debug.Log("[TRACER] 1. Máy bắn gọi RPC_FireGun | Host? " + Runner.IsServer);

        Vector3 fireDirection = (targetPoint - gunFirePoint.position).normalized;
        RPC_FireGun(gunFirePoint.position, fireDirection);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_FireGun(Vector3 firePos, Vector3 direction)
    {
        Debug.Log("[TRACER] 2. Host nhận RPC_FireGun");

        // BÍ QUYẾT BẮT TRÚNG: Đẩy điểm bắt đầu của tia ra phía trước 2 mét
        // để đạn không bao giờ va chạm vào chính chiếc xe của mình!
        Vector3 rayStart = firePos + (direction * 2.0f);

        // Quét một ống trụ bán kính 0.8m quét TẤT CẢ MỌI LAYER (Mask = -1)
        if (Physics.SphereCast(rayStart, 0.8f, direction, out RaycastHit hit, 200f, -1))
        {
            // IN LOG KIỂM TRA ĐẠN CHẠM VÀO ĐÂU
            Debug.Log($"<color=white>[HITSCAN] Đạn vừa va chạm vào: <b>{hit.collider.name}</b> (Tag: {hit.collider.tag})</color>");

            // 1. Kiểm tra trúng Khối hàng Cargo
            CargoManager cargo = hit.collider.GetComponentInParent<CargoManager>();
            if (cargo != null)
            {
                if (MyTeam == PlayerTeam.Hunter)
                {
                    cargo.RPC_TakeDamage(gunDamage);
                    Debug.Log("<color=yellow><b>>> TRÚNG CARGO! ĐÃ TRỪ MÁU! <<</b></color>");
                }
                else
                {
                    Debug.Log("<color=cyan>[HITSCAN] Phe Bảo Vệ bắn trúng Cargo -> Miễn nhiễm sát thương.</color>");
                }
            }

            // 2. Kiểm tra trúng Xe đối thủ
            NetworkCarController hitCar = hit.collider.GetComponentInParent<NetworkCarController>();
            if (hitCar != null && hitCar != this)
            {
                if (hitCar.MyTeam != this.MyTeam)
                {
                    hitCar.RPC_TakeDamage(gunDamage);
                    Debug.Log($"<color=red><b>>> [CHÍ MẠNG] BẮN TRÚNG ĐỐI THỦ {hitCar.MyTeam}! ĐÃ TRỪ {gunDamage} HP! <<</b></color>");
                }
                else
                {
                    Debug.Log($"<color=green>[HITSCAN] Bắn trúng đồng đội {hitCar.MyTeam} -> Chống bắn nhầm (Friendly Fire)!</color>");
                }
            }
        }
        else
        {
            Debug.Log("<color=gray>[HITSCAN] Đạn bắn trượt vào không khí!</color>");
        }

        RPC_DrawTracer(firePos, firePos + (direction * 150f));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_DrawTracer(Vector3 startPoint, Vector3 endPoint)
    {
        Debug.Log($"[TRACER] 4. enabled={bulletTracer.enabled} | width={bulletTracer.widthMultiplier} | count={bulletTracer.positionCount} | start={startPoint} | end={endPoint} | cam={(Camera.main != null ? Camera.main.transform.position.ToString() : "NULL")}");

        Debug.Log($"[TRACER] 3. Nhận DrawTracer trên {(Runner.IsServer ? "HOST" : "CLIENT")} | " +
          $"lr={(bulletTracer != null)} | worldSpace={(bulletTracer != null && bulletTracer.useWorldSpace)} | " +
          $"shader={(bulletTracer != null && bulletTracer.sharedMaterial != null ? bulletTracer.sharedMaterial.shader.name : "NULL")}");

        // Hàm này chạy trên TẤT CẢ các máy (Host và Client)
        if (bulletTracer != null)
        {
            bulletTracer.enabled = true;
            bulletTracer.SetPosition(0, startPoint);
            bulletTracer.SetPosition(1, endPoint);
            tracerTimer = tracerDisplayTime; // Bật đồng hồ chớp tắt
        }
    }

    // CƠ CHẾ KHOAN
    // CHỈ chạy trên StateAuthority (gọi từ FixedUpdateNetwork)
    void ServerDrillTick()
    {
        drillTickAccum += Runner.DeltaTime;
        if (drillTickAccum < drillTickInterval) return;

        float dmg = drillDps * drillTickAccum;
        drillTickAccum = 0f;

        Vector3 tip = transform.TransformPoint(drillTipLocalOffset);
        int n = Physics.OverlapSphereNonAlloc(tip, drillRadius, drillHits, ~0, QueryTriggerInteraction.Ignore);

        drillAlreadyHit.Clear();
        for (int i = 0; i < n; i++)
        {
            Collider c = drillHits[i];

            NetworkCarController car = c.GetComponentInParent<NetworkCarController>();
            if (car != null)
            {
                if (car == this || car.IsDead || car.IsArmored || car.MyTeam == MyTeam) continue;
                if (drillAlreadyHit.Add(car)) car.RPC_TakeDamage(dmg);
                continue;
            }

            CargoManager cargo = c.GetComponentInParent<CargoManager>();
            if (cargo != null && MyTeam == PlayerTeam.Hunter && drillAlreadyHit.Add(cargo))
            {
                cargo.RPC_TakeDamage(dmg);
            }
        }
    }

    //Các hàm bắn tên lửa
    bool TryGetMissileTarget(out Vector3 target)
    {
        target = default;
        TrackPath tp = TrackPath.Instance;
        if (tp == null || tp.Length <= 0f) return false;
        float d = tp.ProjectDistance(transform.position, ref trackHint);
        target = tp.GetPoint(d + missileTargetAhead);
        return true;
    }

    // CHỈ chạy trên StateAuthority
    void ServerFireMissile()
    {
        if (!TryGetMissileTarget(out Vector3 target))
        {
            Debug.LogWarning("[MISSILE] Không có TrackPath trong scene, chưa bắn được.");
            return;
        }
        Vector3 start = transform.TransformPoint(missileLaunchLocalOffset);
        Vector3 dir = target - start;
        Quaternion rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : transform.rotation;

        HasMissile = false;
        PlayerTeam team = MyTeam;
        int tick = Runner.Tick.Raw;
        float dt = Runner.DeltaTime;
        Runner.Spawn(missilePrefab, start, rot, null, (runner, obj) =>
        {
            obj.GetComponent<NetworkMissile>().Init(start, target, team, tick, dt);
        });
    }

    // Vẽ vùng chạm khi chọn xe trong Scene/Game view lúc chạy, để chỉnh drillTipLocalOffset
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(drillTipLocalOffset), drillRadius);
    }
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (blinkMat != null) Destroy(blinkMat);
    }

    // Xe chết: tắt collider + đóng băng để không rơi xuyên đất. Gọi lặp lại vô hại.
    void ApplyAliveState(bool alive)
    {
        if (aliveStateApplied == alive || rb == null) return;
        aliveStateApplied = alive;
        if (bodyCollider != null) bodyCollider.enabled = alive;
        rb.constraints = alive ? originalConstraints : RigidbodyConstraints.FreezeAll;
    }

    // CHỈ gọi trên StateAuthority. Mỗi xe chỉ giữ 1 power-up.
    void ClearAllPowerUps()
    {
        if (IsArmored) { IsArmored = false; rb.mass = originalMass; }

        IsArmed = false;
        IsDrilling = false;
        DrillTimer = default;
        drillTickAccum = 0f;
        HasMissile = false;
        // Mốc A (Drill) và Mốc B (Missile) sẽ thêm dòng vào đây.


    }
}