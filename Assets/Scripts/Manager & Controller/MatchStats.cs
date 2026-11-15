using System.Collections.Generic;
using UnityEngine;
using Fusion;

// Thống kê trong ván. CHỈ ghi trên server. Kết quả cuối ván được copy vào biến mạng của MatchManager.
public static class MatchStats
{
    public static float W_DmgEnemy = 1.0f, W_DmgCargo = 1.0f, W_Drift = 0.7f, W_Tow = 1.0f, W_Guardian = 1.0f;

    class Accum { public float dmgEnemy, dmgCargo, drift, tow, guardian; }
    static readonly Dictionary<PlayerRef, Accum> acc = new Dictionary<PlayerRef, Accum>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { acc.Clear(); }

    public static void Reset() { acc.Clear(); }

    static Accum Get(PlayerRef p)
    {
        if (!acc.TryGetValue(p, out Accum a)) { a = new Accum(); acc[p] = a; }
        return a;
    }

    // Giữ nguyên chữ ký để không phải sửa các nơi gọi. 'attackerTeam' và 'victim' hiện không dùng.
    public static void AddEnemyDamage(PlayerRef attacker, NetworkCarController.PlayerTeam attackerTeam,
                                      NetworkCarController victim, float amount)
    {
        if (attacker.IsNone || amount <= 0f) return;
        Get(attacker).dmgEnemy += amount;
    }

    public static void AddCargoDamage(PlayerRef attacker, float amount)
    {
        if (attacker.IsNone || amount <= 0f) return;
        Get(attacker).dmgCargo += amount;
    }

    public static void AddGuardian(PlayerRef defender, float amount)
    {
        if (defender.IsNone || amount <= 0f) return;
        Get(defender).guardian += amount;
    }

    public static void AddDrift(PlayerRef p, float seconds) { if (!p.IsNone) Get(p).drift += seconds; }
    public static void AddTow(PlayerRef p, float seconds) { if (!p.IsNone) Get(p).tow += seconds; }

    static float Norm(float v, float max) { return max > 0f ? v / max : 0f; }

    // Gọi một lần cuối ván (có cấp phát bộ nhớ, chấp nhận được)
    public static PlayerStat[] Build(List<PlayerRef> players, List<bool> hunters)
    {
        int n = players.Count;
        var res = new PlayerStat[n];
        float[] maxH = new float[5];
        float[] maxD = new float[5];

        for (int i = 0; i < n; i++)
        {
            Accum a = Get(players[i]);
            res[i] = new PlayerStat
            {
                Player = players[i],
                IsHunter = hunters[i],
                DmgEnemy = a.dmgEnemy,
                DmgCargo = a.dmgCargo,
                DriftSec = a.drift,
                TowSec = a.tow,
                Guardian = a.guardian
            };
            float[] mx = hunters[i] ? maxH : maxD;
            mx[0] = Mathf.Max(mx[0], a.dmgEnemy);
            mx[1] = Mathf.Max(mx[1], a.dmgCargo);
            mx[2] = Mathf.Max(mx[2], a.drift);
            mx[3] = Mathf.Max(mx[3], a.tow);
            mx[4] = Mathf.Max(mx[4], a.guardian);
        }

        for (int i = 0; i < n; i++)
        {
            float[] mx = res[i].IsHunter ? maxH : maxD;
            res[i].Score = W_DmgEnemy * Norm(res[i].DmgEnemy, mx[0])
                         + W_DmgCargo * Norm(res[i].DmgCargo, mx[1])
                         + W_Drift * Norm(res[i].DriftSec, mx[2])
                         + W_Tow * Norm(res[i].TowSec, mx[3])
                         + W_Guardian * Norm(res[i].Guardian, mx[4]);
        }
        return res;
    }
}