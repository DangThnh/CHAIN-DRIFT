using System.Collections.Generic;
using UnityEngine;
using Fusion;

// Scoreboard TẠM bằng IMGUI. Giai đoạn 5 thay bằng uGUI + animation MVP.
public class ResultUI : MonoBehaviour
{
    public float headlineSeconds = 3f;
    public float mvpSeconds = 4f;
    public float columnInterval = 1.2f;

    static readonly string[] Titles =
    { "SÁT THƯƠNG LÊN ĐỊCH", "SÁT THƯƠNG LÊN XE HÀNG", "THỜI GIAN DRIFT", "THỜI GIAN KÉO HÀNG", "HỘ TỐNG" };

    private readonly List<PlayerStat> all = new List<PlayerStat>();
    private readonly List<PlayerStat>[] columns = new List<PlayerStat>[5];
    private bool shown;
    private float shownAt;
    private GUIStyle big, mid, small;

    static float Value(PlayerStat s, int k)
    {
        switch (k)
        {
            case 0: return s.DmgEnemy;
            case 1: return s.DmgCargo;
            case 2: return s.DriftSec;
            case 3: return s.TowSec;
            default: return s.Escort;
        }
    }

    static string Fmt(float v, int k)
    {
        return (k == 2 || k == 3) ? v.ToString("0.0") + "s" : Mathf.RoundToInt(v).ToString();
    }

    void Update()
    {
        MatchManager m = MatchManager.Instance;
        bool inResult = m != null && m.Runner != null && m.State == MatchState.Result;
        if (inResult && !shown) { shown = true; shownAt = Time.time; Collect(m); }
        else if (!inResult) shown = false;
    }

    void Collect(MatchManager m)
    {
        all.Clear();
        for (int i = 0; i < MatchManager.MaxSlots; i++)
            if (m.TryGetResultStat(i, out PlayerStat s)) all.Add(s);

        for (int k = 0; k < 5; k++)
        {
            int key = k;
            if (columns[k] == null) columns[k] = new List<PlayerStat>();
            columns[k].Clear();
            for (int i = 0; i < all.Count; i++)
                if (Value(all[i], key) > 0f) columns[k].Add(all[i]);
            columns[k].Sort((a, b) => Value(b, key).CompareTo(Value(a, key)));
        }
    }

    void OnGUI()
    {
        Color old = GUI.contentColor;
        GUI.contentColor = Color.black;
        DrawGUI();
        GUI.contentColor = old;
    }

    void EnsureStyles()
    {
        if (big != null) return;
        big = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        mid = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        small = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
    }

    static string Name(PlayerRef p, PlayerRef me, PlayerRef mvp)
    {
        string s = "Người chơi " + p.RawEncoded;
        if (p == me) s += " (bạn)";
        if (p == mvp) s += " [MVP]";
        return s;
    }

    void DrawGUI()
    {
        if (!shown) return;
        MatchManager m = MatchManager.Instance;
        if (m == null || m.Runner == null) return;
        EnsureStyles();

        float t = Time.time - shownAt;
        float w = Screen.width;
        PlayerRef me = m.Runner.LocalPlayer;

        // Nền sáng để chữ đen dễ đọc
        Color c = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        GUI.DrawTexture(new Rect(0, 0, w, Screen.height), Texture2D.whiteTexture);
        GUI.color = c;

        // 1. Phe thắng, lý do, thời gian, kết quả của bản thân
        bool hunterWon = m.WinnerTeam == NetworkCarController.PlayerTeam.Hunter;
        GUI.Label(new Rect(0, 30, w, 45), hunterWon ? "PHE PHÁ ĐÁM CHIẾN THẮNG!" : "PHE BẢO VỆ CHIẾN THẮNG!", big);

        string reason = "";
        switch ((WinReason)m.WinReasonCode)
        {
            case WinReason.CargoReachedFinish: reason = "Xe hàng đã về đích!"; break;
            case WinReason.CargoDestroyed: reason = "Xe hàng đã bị phá hủy!"; break;
            case WinReason.Debug: reason = "(Kết thúc thủ công để test)"; break;
        }
        int sec = Mathf.RoundToInt(m.MatchSeconds);
        GUI.Label(new Rect(0, 75, w, 30), $"{reason}   Thời gian ván: {sec / 60:00}:{sec % 60:00}", mid);

        string mine = "Bạn quan sát ván này";
        for (int i = 0; i < all.Count; i++)
            if (all[i].Player == me) { mine = (all[i].IsHunter == hunterWon) ? "BẠN ĐÃ THẮNG" : "BẠN ĐÃ THUA"; break; }
        GUI.Label(new Rect(0, 105, w, 30), mine, mid);

        // 2. MVP
        float tMvp = headlineSeconds;
        if (t >= tMvp)
        {
            string mvp = "Không có MVP";
            for (int i = 0; i < all.Count; i++)
                if (all[i].Player == m.MvpPlayer)
                {
                    mvp = $"MVP: Người chơi {all[i].Player.RawEncoded} ({(all[i].IsHunter ? "Phá đám" : "Bảo vệ")}) - {all[i].Score:0.0} điểm";
                    break;
                }
            GUI.Label(new Rect(0, 150, w, 40), mvp, big);
        }

        // 3. Các cột chỉ số, lần lượt hiện ra
        float tCols = headlineSeconds + mvpSeconds;
        float colW = (w - 40f) / 5f;
        for (int k = 0; k < 5; k++)
        {
            if (t < tCols + k * columnInterval) continue;
            float x = 20f + k * colW;
            GUI.Label(new Rect(x, 230, colW, 28), Titles[k], mid);
            var list = columns[k];
            if (list == null || list.Count == 0)
            {
                GUI.Label(new Rect(x + 8, 262, colW, 24), "-", small);
                continue;
            }
            for (int i = 0; i < list.Count; i++)
                GUI.Label(new Rect(x + 8, 262 + i * 24, colW - 8, 24),
                          $"{i + 1}. {Name(list[i].Player, me, m.MvpPlayer)}: {Fmt(Value(list[i], k), k)}", small);
        }

        // Đếm ngược về sảnh, Host có thể bỏ qua
        float by = Screen.height - 70f;
        GUI.Label(new Rect(0, by, w, 24), $"Về sảnh chờ sau {Mathf.CeilToInt(m.ResultSecondsLeft)}s", mid);
        if (m.Runner.LocalPlayer == m.HostPlayer)
            if (GUI.Button(new Rect(w * 0.5f - 100f, by + 26f, 200f, 32f), "VỀ SẢNH CHỜ")) m.RequestReturnToLobby();
    }
}