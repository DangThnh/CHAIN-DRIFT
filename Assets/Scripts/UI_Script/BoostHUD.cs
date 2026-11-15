using UnityEngine;
using Fusion;

// HUD TẠM bằng IMGUI: thanh boost của người chơi trên máy này. Giai đoạn 5 thay bằng uGUI.
public class BoostHUD : MonoBehaviour
{
    private NetworkCarController me;
    private string lastPower = "";
    private float toastUntil;
    private string toastText = "";

    void OnGUI()
    {
        Color oldContent = GUI.contentColor;
        Color oldColor = GUI.color;
        GUI.contentColor = Color.black;
        DrawGUI();
        GUI.contentColor = oldContent;
        GUI.color = oldColor;
    }

    void DrawGUI()
    {
        MatchManager mm = MatchManager.Instance;
        if (mm == null || mm.Runner == null || mm.State != MatchState.Playing) { me = null; lastPower = ""; return; }

        if (me != null && !me.IsReady) me = null;
        if (me == null)
        {
            var all = NetworkCarController.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && c.IsReady && c.HasInputAuthority) { me = c; break; }
            }
        }
        if (me == null || me.IsDead) return;

        // Thông báo khi vừa nhận power-up (từ boost hoặc hộp quà)
        string now = me.PowerUpName;
        if (now != lastPower)
        {
            if (now != "") { toastText = "NHẬN ĐƯỢC: " + now.ToUpper(); toastUntil = Time.time + 2f; }
            lastPower = now;
        }
        if (Time.time < toastUntil)
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.7f, 400f, 30f), toastText);

        float w = 320f, h = 18f;
        float x = Screen.width * 0.5f - w * 0.5f;
        float y = Screen.height - 60f;

        bool paused = me.IsTowingCargo || me.HasAnyPowerUp;
        string status;
        if (me.IsTowingCargo) status = "BOOST: người kéo hàng không tích boost";
        else if (me.HasAnyPowerUp) status = "BOOST: tạm dừng (đang giữ " + me.PowerUpName + ")";
        else status = "BOOST " + Mathf.RoundToInt(me.DriftCharge * 100f) + "% - drift để tích";
        GUI.Label(new Rect(x, y - 24f, w, 22f), status);

        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = paused ? new Color(0.6f, 0.6f, 0.6f, 0.9f) : new Color(1f, 0.65f, 0.1f, 0.95f);
        GUI.DrawTexture(new Rect(x + 2f, y + 2f, (w - 4f) * Mathf.Clamp01(me.DriftCharge), h - 4f), Texture2D.whiteTexture);
    }
}