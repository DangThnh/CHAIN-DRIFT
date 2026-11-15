using UnityEngine;
using UnityEngine.UI;

public class PlayerHP_UI : MonoBehaviour
{
    private Slider hpSlider;
    private NetworkCarController myCar;

    void Start()
    {
        hpSlider = GetComponent<Slider>();
    }

    void Update()
    {
        if (hpSlider == null) return;

        // Xe đã bị hủy (hết ván, thoát phòng): bỏ tham chiếu, KHÔNG đọc biến [Networked]
        if (myCar != null && !myCar.IsReady) myCar = null;

        // Tìm xe của chính mình trong danh sách tĩnh (không cấp phát bộ nhớ)
        if (myCar == null)
        {
            var all = NetworkCarController.All;
            for (int i = 0; i < all.Count; i++)
            {
                var car = all[i];
                if (car != null && car.IsReady && car.HasInputAuthority)
                {
                    myCar = car;
                    hpSlider.maxValue = myCar.maxHealth;
                    break;
                }
            }
        }

        hpSlider.value = myCar != null ? myCar.CurrentHealth : 0f;
    }
}