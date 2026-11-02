using UnityEngine;
using Fusion;

// Camera quan sát cho người vào muộn. Chỉ chạy cục bộ, không có đồng bộ mạng.
public class SpectatorController : MonoBehaviour
{
    public CarCameraController cam;   // để trống = tự tìm

    private NetworkCarController current;
    private bool wasSpectating;

    static bool IsValid(NetworkCarController c)
    {
        return c != null && c.Object != null && c.Object.IsValid;
    }

    void Update()
    {
        MatchManager m = MatchManager.Instance;
        bool spectating = m != null && m.Runner != null
            && m.State == MatchState.Playing
            && m.TryGetLocalSlot(out PlayerSlot s) && !s.InMatch;

        if (cam == null) cam = FindFirstObjectByType<CarCameraController>();
        if (cam == null) return;

        if (!spectating)
        {
            if (wasSpectating)
            {
                cam.StopSpectating();
                current = null;
                wasSpectating = false;
            }
            return;
        }
        wasSpectating = true;

        if (!IsValid(current)) Pick(FirstValid());

        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
            Step(+1);
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            Step(-1);
    }

    NetworkCarController FirstValid()
    {
        var list = NetworkCarController.All;
        for (int i = 0; i < list.Count; i++)
            if (IsValid(list[i])) return list[i];
        return null;
    }

    void Step(int dir)
    {
        var list = NetworkCarController.All;
        int n = list.Count;
        if (n == 0) return;
        int start = IsValid(current) ? list.IndexOf(current) : -1;
        for (int k = 1; k <= n; k++)
        {
            int i = ((start + dir * k) % n + n) % n;
            if (IsValid(list[i])) { Pick(list[i]); return; }
        }
    }

    void Pick(NetworkCarController car)
    {
        current = car;
        if (car == null) return;
        // Bám theo cục Visuals (đã nội suy mượt), giống camera của người chơi
        Transform t = car.chassisMesh != null ? car.chassisMesh.parent : car.transform;
        cam.SetSpectateTarget(t);
    }

    void OnGUI()
    {
        if (!wasSpectating) return;
        string who = "(chưa có xe nào)";
        if (IsValid(current))
        {
            string team = current.MyTeam == NetworkCarController.PlayerTeam.Hunter ? "Phá đám" : "Bảo vệ";
            who = $"Người chơi {current.Object.InputAuthority.RawEncoded} ({team})";
        }
        GUI.Label(new Rect(Screen.width * 0.5f - 220f, 20f, 440f, 25f),
                  $"ĐANG XEM: {who}   [+] / [-] đổi người");
    }
}