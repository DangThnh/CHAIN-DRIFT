using UnityEngine;
using Fusion;

// Sảnh chờ TẠM bằng IMGUI. Giai đoạn 5 thay bằng uGUI (avatar Steam, giao diện đẹp).
public class LobbyUI : MonoBehaviour
{

    void OnGUI()
    {
        Color old = GUI.contentColor;
        GUI.contentColor = Color.black;
        DrawGUI();
        GUI.contentColor = old;
    }

    void DrawGUI()
    {
        MatchManager m = MatchManager.Instance;
        if (m == null || m.Runner == null) return;

        bool iAmHost = m.Runner.LocalPlayer == m.HostPlayer;
        bool hasMine = m.TryGetLocalSlot(out PlayerSlot mine);

        if (m.State == MatchState.Playing)
        {
            string role = (hasMine && mine.InMatch)
                ? (mine.IsHunter ? "PHÁ ĐÁM (Hunter)" : "BẢO VỆ (Defender)")
                : "ĐANG QUAN SÁT";
            GUI.Label(new Rect(20, 60, 400, 25), "Vai trò: " + role);
            if (iAmHost) GUI.Label(new Rect(20, 85, 400, 25), "[F9] Kết thúc ván (tạm)");
            return;
        }

        if (m.State == MatchState.Result) return;   // ResultUI vẽ scoreboard

        float x = 20f, y = 70f;
        GUI.Box(new Rect(x, y, 330, 40 + MatchManager.MaxSlots * 24), "SẢNH CHỜ");
        int row = 0;
        for (int i = 0; i < MatchManager.MaxSlots; i++)
        {
            if (!m.TryGetSlot(i, out PlayerSlot s)) continue;
            bool host = s.Player == m.HostPlayer;
            string status = host ? "Chủ phòng" : (s.Ready ? "Sẵn sàng" : "Chưa sẵn sàng");
            string you = s.Player == m.Runner.LocalPlayer ? " (bạn)" : "";
            GUI.Label(new Rect(x + 10, y + 28 + row * 24, 310, 22), $"Người chơi {s.Player.RawEncoded}{you} - {status}");
            row++;
        }

        m.GetCounts(out int total, out int ready, out int needReady);
        float bx = x + 350f;
        GUI.Label(new Rect(bx, y, 240, 22), $"Sẵn sàng: {ready}/{needReady}  |  Tổng: {total}/{MatchManager.MaxSlots}");

        if (iAmHost)
        {
            bool can = m.CanStart(out string reason);
            GUI.enabled = can;
            if (GUI.Button(new Rect(bx, y + 28, 200, 60), "BẮT ĐẦU")) m.RequestStart();
            GUI.enabled = true;
            if (!can) GUI.Label(new Rect(bx, y + 94, 260, 22), reason);
        }
        else if (hasMine)
        {
            if (GUI.Button(new Rect(bx, y + 28, 200, 60), mine.Ready ? "HỦY SẴN SÀNG" : "SẴN SÀNG"))
                m.RequestReady(!mine.Ready);
        }
    }
}