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
        // Quét tìm xe của chính mình nếu chưa thấy
        if (myCar == null)
        {
            NetworkCarController[] allCars = FindObjectsOfType<NetworkCarController>();
            foreach (var car in allCars)
            {
                if (car.HasInputAuthority) // Đúng xe của tôi cầm lái!
                {
                    myCar = car;
                    hpSlider.maxValue = myCar.maxHealth;
                    break;
                }
            }
        }

        // Cập nhật máu liên tục
        if (myCar != null)
        {
            hpSlider.value = myCar.CurrentHealth;
        }
    }
}