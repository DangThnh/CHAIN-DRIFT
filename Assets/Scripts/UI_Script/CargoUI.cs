using UnityEngine;
using UnityEngine.UI;

public class CargoUI : MonoBehaviour
{
    private Slider hpSlider;
    private CanvasGroup canvasGroup;

    void Start()
    {
        hpSlider = GetComponent<Slider>();

        // Dùng CanvasGroup để ẩn/hiện thanh máu nhanh gọn
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Mặc định giấu thanh máu đi (Chờ mạng load xong mới quyết định)
        canvasGroup.alpha = 0f;
    }

    void Update()
    {
        // 1. TÌM KHỐI HÀNG TRÊN SCENE
        CargoManager cargo = FindObjectOfType<CargoManager>();
        if (cargo == null) return;

        // 2. TÌM XE CỦA MÌNH ĐỂ BIẾT MÌNH THUỘC PHE NÀO
        NetworkCarController myCar = null;
        NetworkCarController[] allCars = FindObjectsOfType<NetworkCarController>();
        foreach (var car in allCars)
        {
            if (car.HasInputAuthority)
            {
                myCar = car;
                break;
            }
        }

        if (myCar == null) return;

        // 3. LUẬT ẨN/HIỆN THANH MÁU
        // CHỈ khi mình là Defender (Bảo vệ), mình mới được nhìn thấy máu!
        if (myCar.MyTeam == NetworkCarController.PlayerTeam.Defender)
        {
            canvasGroup.alpha = 1f; // Hiện lên

            // Cập nhật số máu
            hpSlider.maxValue = cargo.maxHealth;
            hpSlider.value = cargo.CurrentHealth;
        }
        else
        {
            // Phe Hunter (Phá đám) -> Tắt UI đi, mù màu!
            canvasGroup.alpha = 0f;
        }
    }
}