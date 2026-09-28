using UnityEngine;

public class CarCameraController : MonoBehaviour
{
    // Đổi tên thành CarCamPreset và cho vào trong class để tránh đụng độ với Unity nội bộ
    [System.Serializable]
    public struct CarCamPreset
    {
        public string viewName;
        public float distance;
        public float height;
    }

    [Header("Target Link")]
    public Transform carTarget;
    private Rigidbody carRb;

    [Header("Camera Views (Bấm C để đổi)")]
    public CarCamPreset[] cameraViews;          // Sử dụng tên mới
    public float viewTransitionSpeed = 10f;
    private int currentViewIndex = 0;

    private float currentTargetDistance;
    private float currentTargetHeight;

    [Header("View Settings")]
    [Tooltip("Độ cao điểm camera nhìn vào (tính từ tâm xe)")]
    public float lookAtHeightOffset = 1.0f;

    [Header("Speed Dynamics")]
    public float maxSpeedKmh = 120f;
    public float extraDistanceAtMaxSpeed = 2.0f;
    public float minFOV = 60f;
    public float maxFOV = 80f;

    [Header("Drift Swing (Luật 30 Độ)")]
    public float maxSwingAngle = 30f;
    public float swingMultiplier = 1.5f;
    public float swingSmoothSpeed = 5f;

    [Header("Vertical Damping")]
    public float verticalDamp = 10f;

    private Camera mainCam;
    private float smoothedY;
    private float currentSwingAngle = 0f;
    private float smoothedSpeed = 0f;

    void Start()
    {
        mainCam = GetComponentInChildren<Camera>();

        // Fix lỗi ống kính bị lệch
        mainCam.transform.localPosition = Vector3.zero;
        mainCam.transform.localRotation = Quaternion.identity;

        if (carTarget != null)
        {
            carRb = carTarget.GetComponent<Rigidbody>();
            smoothedY = carTarget.position.y;
        }

        // Khởi tạo góc nhìn mặc định 
        if (cameraViews != null && cameraViews.Length > 0)
        {
            currentTargetDistance = cameraViews[0].distance;
            currentTargetHeight = cameraViews[0].height;
        }
    }

    // Camera sẽ tự động quét tìm xe liên tục cho đến khi tìm thấy xe chính chủ
    void Update()
    {
        // Nhận input phím C
        if (Input.GetKeyDown(KeyCode.C))
        {
            SwitchCameraView();
        }

        //// TỰ ĐỘNG TÌM XE CỦA MÌNH
        //if (carTarget == null)
        //{
        //    NetworkCarController[] allCars = FindObjectsOfType<NetworkCarController>();
        //    foreach (NetworkCarController car in allCars)
        //    {
        //        // Chỉ bám theo xe nếu mình có quyền điều khiển (InputAuthority) xe đó
        //        if (car.Object != null && car.Object.HasInputAuthority)
        //        {
        //            carTarget = car.transform;
        //            carRb = car.GetComponent<Rigidbody>();
        //            smoothedY = carTarget.position.y;
        //            Debug.Log(">> CAMERA ĐÃ TÌM THẤY VÀ KHÓA MỤC TIÊU VÀO XE CHÍNH CHỦ! <<");
        //            break;
        //        }
        //    }
        //}
    }
    public void SetTarget(Transform newTarget)
    {
        carTarget = newTarget;

        // SỬA DÒNG NÀY: Dùng GetComponentInParent thay vì GetComponent
        // Để dù mục tiêu là cục con 'Visuals', nó vẫn tìm thấy Rigidbody ở cục gốc!
        carRb = newTarget.GetComponentInParent<Rigidbody>();

        smoothedY = carTarget.position.y;
        Debug.Log(">> CAMERA ĐÃ NHẬN MỤC TIÊU MƯỢT MÀ! <<");
    }

    void SwitchCameraView()
    {
        if (cameraViews == null || cameraViews.Length <= 1) return;

        currentViewIndex = (currentViewIndex + 1) % cameraViews.Length;
    }

    void LateUpdate()
    {
        if (!carTarget || !carRb) return;

        // 0. CHUYỂN ĐỔI GÓC NHÌN MƯỢT MÀ
        if (cameraViews != null && cameraViews.Length > 0)
        {
            currentTargetDistance = Mathf.Lerp(currentTargetDistance, cameraViews[currentViewIndex].distance, viewTransitionSpeed * Time.deltaTime);
            currentTargetHeight = Mathf.Lerp(currentTargetHeight, cameraViews[currentViewIndex].height, viewTransitionSpeed * Time.deltaTime);
        }

        // 1. TÍNH TOÁN TỐC ĐỘ MƯỢT
        float actualSpeed = carRb.linearVelocity.magnitude * 3.6f;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, actualSpeed, 3f * Time.deltaTime);
        float speedRatio = Mathf.Clamp01(smoothedSpeed / maxSpeedKmh);

        // 2. CHỐNG GIẬT DỌC
        smoothedY = Mathf.Lerp(smoothedY, carTarget.position.y, verticalDamp * Time.deltaTime);
        Vector3 basePos = new Vector3(carTarget.position.x, smoothedY, carTarget.position.z);

        // 3. TÍNH TOÁN GÓC VĂNG DRIFT
        float targetSwingAngle = 0f;

        if (smoothedSpeed > 10f)
        {
            Vector3 flatVelocity = new Vector3(carRb.linearVelocity.x, 0, carRb.linearVelocity.z).normalized;
            Vector3 flatForward = new Vector3(carTarget.forward.x, 0, carTarget.forward.z).normalized;

            float slipAngle = Vector3.SignedAngle(flatForward, flatVelocity, Vector3.up);

            if (Vector3.Dot(carTarget.forward, carRb.linearVelocity) < -0.1f) slipAngle = 0f;

            targetSwingAngle = Mathf.Clamp(slipAngle * swingMultiplier, -maxSwingAngle, maxSwingAngle);
        }

        currentSwingAngle = Mathf.Lerp(currentSwingAngle, targetSwingAngle, swingSmoothSpeed * Time.deltaTime);

        // 4. ĐỊNH VỊ CAMERA
        Vector3 backwardDir = -carTarget.forward;
        backwardDir = Quaternion.Euler(0, currentSwingAngle, 0) * backwardDir;

        float finalDistance = currentTargetDistance + (speedRatio * extraDistanceAtMaxSpeed);
        transform.position = basePos + (backwardDir * finalDistance) + (Vector3.up * currentTargetHeight);

        // 5. KHÓA MỤC TIÊU
        Vector3 targetLookAt = basePos + (carTarget.up * lookAtHeightOffset);
        transform.LookAt(targetLookAt);

        // 6. CẬP NHẬT FOV
        if (mainCam != null)
        {
            mainCam.fieldOfView = Mathf.Lerp(minFOV, maxFOV, speedRatio);
        }
    }
}