using UnityEngine;
using Fusion;

// HUD TẠM bằng IMGUI, chỉ hiện cho Defender. Giai đoạn 5 thay bằng uGUI.
public class TowHUD : MonoBehaviour
{
    private GUIStyle big;
    private CargoManager cargo;

    void OnGUI()
    {
        Color old = GUI.contentColor;
        GUI.contentColor = Color.black;
        DrawGUI();
        GUI.contentColor = old;
    }

    void DrawGUI()
    {
        MatchManager mm = MatchManager.Instance;
        TowManager tm = TowManager.Instance;
        if (mm == null || tm == null || tm.Runner == null) return;
        if (mm.State != MatchState.Playing || tm.Phase == TowPhase.Off) return;

        NetworkCarController me = null;
        var cars = NetworkCarController.All;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            if (c != null && c.Object != null && c.Object.IsValid && c.HasInputAuthority) { me = c; break; }
        }
        if (me == null || me.tow == null || me.MyTeam != NetworkCarController.PlayerTeam.Defender) return;

        if (big == null) { big = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; }

        PlayerRef myRef = me.Object.InputAuthority;
        bool iAmSuccessor = tm.Successor.IsNone || tm.Successor == myRef;
        string who = tm.Successor.IsNone ? "chưa có" : "Player " + tm.Successor.RawEncoded;
        float phaseRem = tm.PhaseTimer.RemainingTime(tm.Runner) ?? 0f;
        string msg = null;

        if (me.IsTowingCargo)
        {
            msg = "BẠN ĐANG KÉO HÀNG";
            if (tm.Phase == TowPhase.Warning) msg += $"\nNgười kế nhiệm: {who}";
        }
        else
        {
            switch (tm.Phase)
            {
                case TowPhase.Warning:
                    msg = (tm.Successor == myRef)
                        ? $"BẠN LÀ NGƯỜI KẾ NHIỆM! Chạy tới xe hàng và bấm F ({Mathf.CeilToInt(phaseRem)}s)"
                        : $"Sắp đổi người kéo. Kế nhiệm: {who} ({Mathf.CeilToInt(phaseRem)}s)";
                    break;
                case TowPhase.Need:
                    msg = iAmSuccessor
                        ? $"CẦN NGƯỜI KÉO HÀNG! Bạn được chỉ định, bấm F gần xe hàng. Phạt sau {Mathf.CeilToInt(phaseRem)}s"
                        : $"Chờ {who} nhận hàng ({Mathf.CeilToInt(phaseRem)}s)";
                    break;
                case TowPhase.Penalty:
                    msg = iAmSuccessor
                        ? "XE HÀNG ĐANG MẤT MÁU! Bạn phải kéo hàng ngay (F)!"
                        : $"XE HÀNG ĐANG MẤT MÁU! Chờ {who} nhận hàng.";
                    break;
            }
        }

        if (msg != null)
            GUI.Label(new Rect(Screen.width * 0.5f - 400f, 50f, 800f, 70f), msg, big);

        // Đồng hồ cho CẢ ĐỘI Defender: mỗi người đang kéo và số giây còn lại trước khi phải đổi
        string teamLine = null;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            if (c == null || c.tow == null || c.Object == null || !c.Object.IsValid || !c.IsTowingCargo) continue;
            float t = c.tow.TowTimer.RemainingTime(tm.Runner) ?? 0f;
            string name = c == me ? "BẠN" : "Player " + c.Object.InputAuthority.RawEncoded;
            string line = $"Người kéo: {name} - đổi sau {Mathf.CeilToInt(t)}s";
            teamLine = teamLine == null ? line : teamLine + "\n" + line;
        }
        if (teamLine != null)
            GUI.Label(new Rect(Screen.width * 0.5f - 300f, 120f, 600f, 55f), teamLine, big);

        if (cargo == null) cargo = FindFirstObjectByType<CargoManager>();
        if (cargo != null)
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, 178f, 400f, 25f),
                      $"Máu xe hàng: {Mathf.CeilToInt(cargo.CurrentHealth)}/{cargo.maxHealth}", big);
    }
}