using UnityEngine;

[System.Serializable]
public struct CarCamPreset
{
    public string viewName;
    public float distance;
    public float height;
}

public class CarCameraController : MonoBehaviour
{
    [Header("Target Link")]
    public Transform carTarget;
    private Rigidbody carRb;
    private NetworkCarController carNetworkCtrl; // Lấy script mạng để biết xe có Súng không

    [Header("Camera Views")]
    public CarCamPreset[] cameraViews;
    public float viewTransitionSpeed = 10f;
    private int currentViewIndex = 0;
    private float currentTargetDistance;
    private float currentTargetHeight;

    [Header("View Settings")]
    public float lookAtHeightOffset = 1.0f;
    public float maxSpeedKmh = 120f;
    public float extraDistanceAtMaxSpeed = 2.0f;
    public float minFOV = 60f;
    public float maxFOV = 80f;

    [Header("Drift Swing (Dùng khi KHÔNG CÓ SÚNG)")]
    public float maxSwingAngle = 30f;
    public float swingMultiplier = 1.5f;
    public float swingSmoothSpeed = 5f;

    [Header("Mouse Aiming (Dùng KHI CÓ SÚNG)")]
    public float mouseSensitivity = 3f;
    private float currentMousePan = 0f;
    private float currentMouseTilt = 15f; // Góc nhìn xuống mặc định

    public float aimCameraHeight = 1.8f;   // Độ cao camera khi ngắm bắn (Tùy chỉnh)
    public float aimCameraDistance = 4.0f; // Khoảng cách camera khi ngắm bắn (Tùy chỉnh)

    [Header("Damping")]
    public float verticalDamp = 10f;

    private Camera mainCam;
    private float smoothedY;
    private float currentSwingAngle = 0f;
    private float smoothedSpeed = 0f;

    void Start()
    {
        mainCam = GetComponentInChildren<Camera>();
        mainCam.transform.localPosition = Vector3.zero;
        mainCam.transform.localRotation = Quaternion.identity;

        if (cameraViews != null && cameraViews.Length > 0)
        {
            currentTargetDistance = cameraViews[0].distance;
            currentTargetHeight = cameraViews[0].height;
        }

        // Ẩn chuột lúc đầu
        //Cursor.lockState = CursorLockMode.Locked;
        //Cursor.visible = false;
    }

    public void SetTarget(Transform newTarget)
    {
        carTarget = newTarget;
        carRb = newTarget.GetComponentInParent<Rigidbody>();
        carNetworkCtrl = newTarget.GetComponentInParent<NetworkCarController>();
        smoothedY = carTarget.position.y;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (cameraViews != null && cameraViews.Length > 1)
                currentViewIndex = (currentViewIndex + 1) % cameraViews.Length;
        }

        if (carTarget == null)
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // =======================================================
        // ĐIỀU KHIỂN CHUỘT (CHẾ ĐỘ TỰ DO BẮN SÚNG)
        // =======================================================
        if (carNetworkCtrl != null && carNetworkCtrl.IsArmed && !carNetworkCtrl.IsDead)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                currentMousePan += Input.GetAxis("Mouse X") * mouseSensitivity;
                currentMouseTilt -= Input.GetAxis("Mouse Y") * mouseSensitivity;

                // FIX LỖI KHÔNG NHÌN ĐƯỢC LÊN TRỜI:
                // Cho phép góc tilt âm (-60 độ) để ngước nhìn lên không trung thoải mái!
                currentMouseTilt = Mathf.Clamp(currentMouseTilt, -45f, 70f);
            }
        }
        else
        {
            // Khi không có súng: Giữ góc nhìn hơi chúi 15 độ mặc định
            currentMouseTilt = Mathf.Lerp(currentMouseTilt, 15f, 5f * Time.deltaTime);
        }
    }

    void LateUpdate()
    {
        if (!carTarget || !carRb) return;

        if (cameraViews != null && cameraViews.Length > 0)
        {
            currentTargetDistance = Mathf.Lerp(currentTargetDistance, cameraViews[currentViewIndex].distance, viewTransitionSpeed * Time.deltaTime);
            currentTargetHeight = Mathf.Lerp(currentTargetHeight, cameraViews[currentViewIndex].height, viewTransitionSpeed * Time.deltaTime);
        }

        float actualSpeed = carRb.linearVelocity.magnitude * 3.6f;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, actualSpeed, 3f * Time.deltaTime);
        float speedRatio = Mathf.Clamp01(smoothedSpeed / maxSpeedKmh);

        smoothedY = Mathf.Lerp(smoothedY, carTarget.position.y, verticalDamp * Time.deltaTime);
        Vector3 basePos = new Vector3(carTarget.position.x, smoothedY, carTarget.position.z);

        float finalDistance = currentTargetDistance + (speedRatio * extraDistanceAtMaxSpeed);
        float finalHeight = currentTargetHeight;

        bool isAiming = carNetworkCtrl != null && carNetworkCtrl.IsArmed && !carNetworkCtrl.IsDead;

        // =======================================================
        // TOÁN HỌC ĐỊNH VỊ CAMERA MỚI (TRIỆT TIÊU LỖI LỘN 180 ĐỘ)
        // =======================================================
        if (isAiming)
        {
            finalHeight += 1.2f;
            if (finalDistance < 4f) finalDistance = 4f;

            // Góc xoay tự do của Camera theo chuột
            Quaternion aimRotation = Quaternion.Euler(currentMouseTilt, currentMousePan, 0);

            // Vị trí Camera: Đặt sau lưng xe dựa trên góc nhìn chuột (Dấu trừ để lùi ra sau)
            transform.position = basePos - (aimRotation * Vector3.forward * finalDistance) + (Vector3.up * finalHeight);

            // Camera nhìn thẳng tắp theo hướng chuột
            transform.rotation = aimRotation;
        }
        else
        {
            // KHI LÁI BÌNH THƯỜNG: Bám theo đuôi xe
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

            Quaternion driveRotation = Quaternion.Euler(currentMouseTilt, currentSwingAngle + carTarget.eulerAngles.y, 0);
            transform.position = basePos - (driveRotation * Vector3.forward * finalDistance) + (Vector3.up * finalHeight);

            Vector3 targetLookAt = basePos + (Vector3.up * lookAtHeightOffset);
            transform.LookAt(targetLookAt);

            // ĐỒNG BỘ GÓC CHUỘT: Luôn bám sát hướng đầu xe đang chạy
            // Khi ăn súng, góc chuột lập tức nhìn về phía trước mượt mà, KHÔNG BỊ QUAY NGƯỢC!
            currentMousePan = carTarget.eulerAngles.y;
        }

        if (mainCam != null)
        {
            mainCam.fieldOfView = Mathf.Lerp(minFOV, maxFOV, speedRatio);
        }
    }
}