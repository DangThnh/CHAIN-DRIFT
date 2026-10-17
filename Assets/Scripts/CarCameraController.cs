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

        // =======================================================
        // QUẢN LÝ CHUỘT: TỰ DO Ở SẢNH - KHÓA KHI VÀO GAME
        // =======================================================
        if (carTarget == null)
        {
            // Đang ở sảnh chờ, chưa có xe: HIỆN CHUỘT LÊN ĐỂ BẤM NÚT
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return; // Chưa có xe thì ngắt hàm, không xử lý xoay camera
        }
        else
        {
            // Đã vào game, Camera đã khóa vào xe: GIẤU CHUỘT ĐI ĐỂ CHƠI
            // Mẹo: Cho phép bấm ESC để nhả chuột ra nếu cần
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Input.GetMouseButtonDown(0)) // Click chuột trái để khóa lại
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // =======================================================
        // LOGIC CAMERA (CHỈ QUAY ĐƯỢC KHI CÓ SÚNG)
        // =======================================================
        if (carNetworkCtrl != null && carNetworkCtrl.IsArmed && !carNetworkCtrl.IsDead)
        {
            // Nếu chuột đang bị khóa (đang chơi game), thì mới đọc Input di chuột
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                currentMousePan += Input.GetAxis("Mouse X") * mouseSensitivity;
                currentMouseTilt -= Input.GetAxis("Mouse Y") * mouseSensitivity;
                currentMouseTilt = Mathf.Clamp(currentMouseTilt, 0f, 60f);
            }
        }
        else
        {
            // Mất súng: Reset góc tilt mượt mà về sau đuôi xe
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

        bool isAiming = carNetworkCtrl != null && carNetworkCtrl.IsArmed && !carNetworkCtrl.IsDead;

        Vector3 basePos = new Vector3(carTarget.position.x, smoothedY, carTarget.position.z);
        Vector3 backwardDir = -carTarget.forward;

        if (isAiming)
        {
            // CÓ SÚNG: Tự do hoàn toàn! Vector lùi của camera xoay theo hướng chuột
            backwardDir = Quaternion.Euler(currentMouseTilt, currentMousePan, 0) * Vector3.forward;
        }
        else
        {
            // KHÔNG SÚNG: Quỹ đạo Drift Swing
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

            backwardDir = Quaternion.Euler(currentMouseTilt, currentSwingAngle + carTarget.eulerAngles.y, 0) * -Vector3.forward;

            // ĐỒNG BỘ GÓC CHUỘT: Lưu lại góc nhìn hiện tại để lúc ăn súng không bị quay ngược 180 độ
            currentMousePan = currentSwingAngle + carTarget.eulerAngles.y + 180f;
        }

        // ĐỊNH VỊ CAMERA
        float finalDistance = currentTargetDistance + ((smoothedSpeed / maxSpeedKmh) * extraDistanceAtMaxSpeed);
        Vector3 targetCamPos = basePos + (backwardDir * finalDistance);
        targetCamPos.y = basePos.y + currentTargetHeight;
        transform.position = targetCamPos;

        // =======================================================
        // BÍ QUYẾT GIẢI PHÓNG CAMERA NẰM Ở ĐÂY:
        // =======================================================
        if (isAiming)
        {
            // Nếu có súng: Camera KHÔNG LookAt xe nữa! Camera nhìn thẳng theo hướng chuột!
            transform.rotation = Quaternion.Euler(currentMouseTilt, currentMousePan, 0);
        }
        else
        {
            // Nếu không có súng: Mới khóa LookAt vào đít xe
            Vector3 targetLookAt = basePos + (Vector3.up * lookAtHeightOffset);
            transform.LookAt(targetLookAt);
        }

        if (mainCam != null)
        {
            mainCam.fieldOfView = Mathf.Lerp(minFOV, maxFOV, smoothedSpeed / maxSpeedKmh);
        }
    }
}