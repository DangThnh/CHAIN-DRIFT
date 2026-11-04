using System.Collections.Generic;
using UnityEngine;
using Fusion;

public enum TowPhase : byte { Off = 0, Normal = 1, Warning = 2, Need = 3, Penalty = 4 }

// Gắn cùng object [MATCH_MANAGER]. Toàn bộ logic quyết định chạy trên server.
public class TowManager : NetworkBehaviour
{
    public static TowManager Instance { get; private set; }

    [Header("Luật đổi người kéo")]
    public float towDuration = 60f;          // Thời gian một người kéo
    public float warningSeconds = 10f;       // Báo trước khi đứt dây
    public float initialGraceSeconds = 20f;  // Chờ nhận hàng lúc đầu ván trước khi bị phạt
    public float needGraceSeconds = 10f;     // Chờ sau khi mất người kéo giữa ván
    public float penaltyDps = 20f;           // Máu Cargo mất mỗi giây khi bị phạt
    public int maxTowers = 2;

    [Networked] public TowPhase Phase { get; set; }
    [Networked] public PlayerRef Successor { get; set; }
    [Networked] public TickTimer PhaseTimer { get; set; }   // Warning: giờ dây đứt. Need: giờ bắt đầu phạt.

    private readonly Dictionary<PlayerRef, int> towCount = new Dictionary<PlayerRef, int>();
    private CargoManager cargo;

    public override void Spawned() { Instance = this; }
    public override void Despawned(NetworkRunner runner, bool hasState) { if (Instance == this) Instance = null; }

    // ---------------- Vòng lặp (server) ----------------
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || Phase == TowPhase.Off) return;

        var all = NetworkCarController.All;
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (IsLive(c) && c.IsTowingCargo) MatchStats.AddTow(c.Object.InputAuthority, Runner.DeltaTime);
        }

        CountTowers(out int towers, out NetworkCarController sole);

        // A. Mất hết người kéo (chết, thoát phòng, hết giờ)
        if (towers == 0 && (Phase == TowPhase.Normal || Phase == TowPhase.Warning))
            EnterNeed(needGraceSeconds);

        switch (Phase)
        {
            case TowPhase.Normal:
                if (towers >= 2) ReleaseExpiredTowers();
                else if (towers == 1) CheckWarning(sole);
                break;

            case TowPhase.Warning:
                if (!ValidateSuccessor())
                {
                    // Không còn ai kế nhiệm: gia hạn cho người kéo hiện tại
                    if (sole != null) sole.tow.ServerSetTimer(towDuration);
                    Phase = TowPhase.Normal;
                    Successor = default;
                    break;
                }
                if (sole != null && sole.tow.TowTimer.Expired(Runner))
                {
                    sole.tow.ServerRelease();
                    Phase = TowPhase.Penalty;
                    Debug.Log("[TOW] Hết giờ, chưa ai nhận hàng. Xe hàng bắt đầu mất máu!");
                }
                break;

            case TowPhase.Need:
                ValidateSuccessor();
                if (PhaseTimer.Expired(Runner))
                {
                    Phase = TowPhase.Penalty;
                    Debug.Log("[TOW] Hết thời gian chờ. Xe hàng bắt đầu mất máu!");
                }
                break;

            case TowPhase.Penalty:
                ValidateSuccessor();
                if (cargo == null) cargo = FindFirstObjectByType<CargoManager>();
                if (cargo != null) cargo.ServerDamage(penaltyDps * Runner.DeltaTime, false);
                break;
        }
    }

    void CheckWarning(NetworkCarController sole)
    {
        if (sole == null || sole.tow == null) return;
        float? rem = sole.tow.TowTimer.RemainingTime(Runner);
        if (!rem.HasValue) return;
        if (rem.Value > Mathf.Min(warningSeconds, towDuration - 1f)) return;

        PlayerRef next = PickSuccessor();
        if (next.IsNone)
        {
            sole.tow.ServerSetTimer(towDuration);   // không có ai thay (vd test 2 người): gia hạn, không phạt
            Debug.Log("[TOW] Không có người kế nhiệm, gia hạn người kéo hiện tại.");
            return;
        }
        SetSuccessor(next);
        PhaseTimer = sole.tow.TowTimer;
        Phase = TowPhase.Warning;
    }

    void ReleaseExpiredTowers()
    {
        var cars = NetworkCarController.All;
        int towers = CountTowers();
        for (int i = 0; i < cars.Count && towers >= 2; i++)
        {
            var c = cars[i];
            if (!IsLive(c) || !c.IsTowingCargo) continue;
            if (c.tow.TowTimer.Expired(Runner))
            {
                c.tow.ServerRelease();
                towers--;
            }
        }
    }

    void EnterNeed(float graceSeconds)
    {
        Phase = TowPhase.Need;
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, graceSeconds);
        PlayerRef next = PickSuccessor();
        if (next.IsNone) Successor = default; else SetSuccessor(next);
    }

    // true nếu có người kế nhiệm hợp lệ (chọn lại nếu cái cũ không còn hợp lệ). Pha Need/Penalty: vẫn true khi chưa có ai.
    bool ValidateSuccessor()
    {
        if (IsValidSuccessor(Successor)) return true;
        PlayerRef next = PickSuccessor();
        if (next.IsNone) { Successor = default; return Phase == TowPhase.Need || Phase == TowPhase.Penalty; }
        SetSuccessor(next);
        return true;
    }

    void SetSuccessor(PlayerRef p)
    {
        Successor = p;
        towCount.TryGetValue(p, out int c);
        Debug.Log($"[TOW] Người kế nhiệm: Player {p.RawEncoded} (số lượt kéo: {c})");
    }

    // ---------------- API cho NetworkTowController (server) ----------------
    public bool ServerCanAttach(NetworkCarController car)
    {
        if (Phase == TowPhase.Off) return true;
        if (car == null || car.MyTeam != NetworkCarController.PlayerTeam.Defender) return false;
        if (CountTowers() >= maxTowers) { Debug.Log("[TOW] Đã đủ người kéo."); return false; }
        if (Phase == TowPhase.Normal) return true;   // tình nguyện làm người kéo thứ hai

        PlayerRef me = car.Object.InputAuthority;
        if (Successor.IsNone || Successor == me) return true;
        Debug.Log("[TOW] Chỉ người kế nhiệm được nhận hàng lúc này.");
        return false;
    }

    public bool ServerCanDetach(NetworkCarController car)
    {
        if (Phase == TowPhase.Off) return true;
        return CountTowers() >= 2;
    }

    public void ServerOnAttached(NetworkCarController car)
    {
        if (Phase == TowPhase.Off || car == null) return;

        PlayerRef me = car.Object.InputAuthority;
        towCount.TryGetValue(me, out int c);
        towCount[me] = c + 1;

        car.tow.ServerSetTimer(towDuration);
        car.ServerClearPowerUps();

        if (Phase == TowPhase.Warning)
        {
            // Chuyển giao: dây người tiền nhiệm đứt ngay
            var cars = NetworkCarController.All;
            for (int i = 0; i < cars.Count; i++)
            {
                var o = cars[i];
                if (o != car && IsLive(o) && o.IsTowingCargo) o.tow.ServerRelease();
            }
            Debug.Log($"[TOW] Chuyển giao hàng cho Player {me.RawEncoded}.");
        }

        if (Phase != TowPhase.Normal)
        {
            Phase = TowPhase.Normal;
            Successor = default;
            PhaseTimer = default;
        }
    }

    public void ServerOnMatchStart()
    {
        towCount.Clear();
        cargo = null;
        Successor = default;
        Phase = TowPhase.Need;
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, initialGraceSeconds);
    }

    public void ServerOnMatchEnd()
    {
        towCount.Clear();
        cargo = null;
        Phase = TowPhase.Off;
        Successor = default;
        PhaseTimer = default;
    }

    // ---------------- Hàm phụ ----------------
    static bool IsLive(NetworkCarController c)
    {
        return c != null && c.tow != null && c.Object != null && c.Object.IsValid;
    }

    int CountTowers() { CountTowers(out int n, out _); return n; }

    void CountTowers(out int towers, out NetworkCarController sole)
    {
        towers = 0; sole = null;
        var cars = NetworkCarController.All;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            if (!IsLive(c) || !c.IsTowingCargo) continue;
            towers++; sole = c;
        }
    }

    bool IsValidSuccessor(PlayerRef p)
    {
        if (p.IsNone) return false;
        var cars = NetworkCarController.All;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            if (!IsLive(c) || c.Object.InputAuthority != p) continue;
            return c.MyTeam == NetworkCarController.PlayerTeam.Defender && !c.IsTowingCargo;
        }
        return false;
    }

    // Ưu tiên: còn sống > ít lượt kéo > ngẫu nhiên
    PlayerRef PickSuccessor()
    {
        PlayerRef best = default;
        int bestCount = int.MaxValue, ties = 0;
        bool bestAlive = false;

        var cars = NetworkCarController.All;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            if (!IsLive(c) || c.IsTowingCargo || c.MyTeam != NetworkCarController.PlayerTeam.Defender) continue;

            PlayerRef p = c.Object.InputAuthority;
            towCount.TryGetValue(p, out int cnt);
            bool alive = !c.IsDead;

            bool better = false, tie = false;
            if (best.IsNone) better = true;
            else if (alive != bestAlive) better = alive;
            else if (cnt != bestCount) better = cnt < bestCount;
            else tie = true;

            if (better) { best = p; bestAlive = alive; bestCount = cnt; ties = 1; }
            else if (tie) { ties++; if (Random.Range(0, ties) == 0) best = p; }
        }
        return best;
    }
}