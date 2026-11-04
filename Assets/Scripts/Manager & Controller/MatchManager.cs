using System.Collections.Generic;
using UnityEngine;
using Fusion;

public enum MatchState : byte { Lobby = 0, Playing = 1, Result = 2 }
public enum WinReason : byte { None = 0, CargoReachedFinish = 1, CargoDestroyed = 2, Debug = 3 }

public struct PlayerSlot : INetworkStruct
{
    public PlayerRef Player;
    public NetworkBool Ready;
    public NetworkBool InMatch;   // true = đang có xe trong ván này
    public NetworkBool IsHunter;

}
public struct PlayerStat : INetworkStruct
{
    public PlayerRef Player;
    public NetworkBool IsHunter;
    public float DmgEnemy;
    public float DmgCargo;
    public float DriftSec;
    public float TowSec;
    public float Escort;
    public float Score;
}

public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }
    public const int MaxSlots = 8;

    [Header("Scene refs")]
    public NetworkLauncher launcher;        // Kéo [NETWORK_MANAGER] vào đây

    [Header("Rules")]
    public int minPlayersToStart = 2;       // Luật thật là 3. Để 1 nếu muốn test một mình
    public bool forceOneHunterWhenTwo = true;
    public int rosterSyncTicks = 10;

    [Header("Debug (TẠM, xóa ở C3)")]
    public KeyCode debugEndKey = KeyCode.F9;

    private MatchState lastState = (MatchState)255;   // 255 = chưa khởi tạo
    private TowManager towManager;

    [Networked] public MatchState State { get; set; }
    [Networked] public PlayerRef HostPlayer { get; set; }
    [Networked, Capacity(MaxSlots)] private NetworkArray<PlayerSlot> Slots => default;

    [Header("Win conditions")]
    public float finishDistance = 10f;   // Cargo cách đích dưới số mét này thì Defender thắng
    public float resultSeconds = 30f;    // Thời gian hiện scoreboard
    public int finishCheckTicks = 5;

    [Networked] public NetworkCarController.PlayerTeam WinnerTeam { get; set; }
    [Networked] public byte WinReasonCode { get; set; }
    [Networked] public float MatchSeconds { get; set; }
    [Networked] public PlayerRef MvpPlayer { get; set; }
    [Networked] private TickTimer ResultTimer { get; set; }
    [Networked, Capacity(MaxSlots)] private NetworkArray<PlayerStat> ResultStats => default;

    private int matchStartTick;
    private int finishHint;
    private bool trackWarned;

    public NetworkObject CargoObject => cargoObj;
    public float ResultSecondsLeft => ResultTimer.RemainingTime(Runner) ?? 0f;

    // Chỉ dùng trên server
    private readonly Dictionary<PlayerRef, NetworkObject> cars = new Dictionary<PlayerRef, NetworkObject>();
    private NetworkObject cargoObj;
    private readonly List<int> participants = new List<int>();

    public override void Spawned()
    {
        towManager = GetComponent<TowManager>();

        Instance = this;
        if (launcher == null) launcher = FindFirstObjectByType<NetworkLauncher>();
        if (Object.HasStateAuthority)
        {
            HostPlayer = Runner.LocalPlayer;
            State = MatchState.Lobby;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        if (State == MatchState.Playing) CheckWinConditions();
        else if (State == MatchState.Result && ResultTimer.Expired(Runner)) ReturnToLobby();

        if (Runner.Tick.Raw % Mathf.Max(1, rosterSyncTicks) != 0) return;
        SyncRoster();
    }

    void CheckWinConditions()
    {
        if (cargoObj == null || !cargoObj.IsValid) return;
        if (Runner.Tick.Raw % Mathf.Max(1, finishCheckTicks) != 0) return;

        TrackPath tp = TrackPath.Instance;
        if (tp == null || tp.Length <= 0f)
        {
            if (!trackWarned) { trackWarned = true; Debug.LogWarning("[MATCH] Không có TrackPath, không xét được vạch đích."); }
            return;
        }
        float d = tp.ProjectDistance(cargoObj.transform.position, ref finishHint);
        if (d >= tp.Length - finishDistance)
            ServerFinishMatch(NetworkCarController.PlayerTeam.Defender, WinReason.CargoReachedFinish);
    }

    public void ServerOnCargoDestroyed()
    {
        ServerFinishMatch(NetworkCarController.PlayerTeam.Hunter, WinReason.CargoDestroyed);
    }

    public void RequestReturnToLobby()
    {
        if (Object.HasStateAuthority && State == MatchState.Result) ReturnToLobby();
    }

    void ReturnToLobby()
    {
        State = MatchState.Lobby;
        Debug.Log("[MATCH] Về sảnh chờ.");
    }

    public bool TryGetResultStat(int i, out PlayerStat s)
    {
        s = default;
        if (i < 0 || i >= MaxSlots) return false;
        s = ResultStats[i];
        return !s.Player.IsNone;
    }

    public override void Render()
    {
        if (State != lastState)
        {
            lastState = State;
            DestructibleObject.ResetAll();   // mỗi máy tự hồi tường của mình
        }

        // TẠM: Host bấm F9 để kết thúc ván
        if (Object.HasStateAuthority && Input.GetKeyDown(debugEndKey))
            ServerFinishMatch(NetworkCarController.PlayerTeam.Defender, WinReason.Debug);
    }

    // ---------------- Danh sách người chơi (server) ----------------
    void SyncRoster()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            PlayerSlot s = Slots[i];
            if (s.Player.IsNone) continue;
            if (!Runner.IsPlayerValid(s.Player))
            {
                DespawnCarOf(s.Player);
                Slots.Set(i, default);
            }
        }

        foreach (PlayerRef p in Runner.ActivePlayers)
        {
            if (FindSlot(p) >= 0) continue;
            int free = FindFreeSlot();
            if (free < 0) continue; // phòng đầy
            Slots.Set(free, new PlayerSlot { Player = p });
        }
    }

    int FindSlot(PlayerRef p)
    {
        for (int i = 0; i < MaxSlots; i++)
            if (!Slots[i].Player.IsNone && Slots[i].Player == p) return i;
        return -1;
    }

    int FindFreeSlot()
    {
        for (int i = 0; i < MaxSlots; i++)
            if (Slots[i].Player.IsNone) return i;
        return -1;
    }

    void DespawnCarOf(PlayerRef p)
    {
        if (cars.TryGetValue(p, out NetworkObject car))
        {
            cars.Remove(p);
            if (car != null) Runner.Despawn(car);
        }
    }

    // ---------------- API cho UI (chạy được trên mọi máy) ----------------
    public bool TryGetSlot(int i, out PlayerSlot s)
    {
        s = default;
        if (i < 0 || i >= MaxSlots) return false;
        s = Slots[i];
        return !s.Player.IsNone;
    }

    public bool TryGetLocalSlot(out PlayerSlot s)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            s = Slots[i];
            if (!s.Player.IsNone && s.Player == Runner.LocalPlayer) return true;
        }
        s = default;
        return false;
    }

    public void GetCounts(out int total, out int ready, out int needReady)
    {
        total = 0; ready = 0; needReady = 0;
        for (int i = 0; i < MaxSlots; i++)
        {
            PlayerSlot s = Slots[i];
            if (s.Player.IsNone) continue;
            total++;
            if (s.Player == HostPlayer) continue;
            needReady++;
            if (s.Ready) ready++;
        }
    }

    public bool CanStart(out string reason)
    {
        reason = "";
        if (State != MatchState.Lobby) { reason = "Ván đang diễn ra"; return false; }
        GetCounts(out int total, out int ready, out int needReady);
        if (total < minPlayersToStart) { reason = $"Cần tối thiểu {minPlayersToStart} người"; return false; }
        if (ready < needReady) { reason = $"Chờ sẵn sàng {ready}/{needReady}"; return false; }
        return true;
    }

    public void RequestReady(bool ready) { RPC_SetReady(ready); }
    public void RequestStart() { if (Object.HasStateAuthority) ServerStartMatch(); }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetReady(NetworkBool ready, RpcInfo info = default)
    {
        if (State != MatchState.Lobby) return;
        int i = FindSlot(info.Source);
        if (i < 0) return;
        PlayerSlot s = Slots[i];
        if (s.Player == HostPlayer) return;
        s.Ready = ready;
        Slots.Set(i, s);
    }

    // ---------------- Vào trận (server) ----------------
    void ServerStartMatch()
    {
        if (launcher == null) { Debug.LogError("[MATCH] Thiếu tham chiếu launcher."); return; }
        if (!CanStart(out string why)) { Debug.Log("[MATCH] Chưa thể bắt đầu: " + why); return; }

        participants.Clear();
        for (int i = 0; i < MaxSlots; i++)
            if (!Slots[i].Player.IsNone) participants.Add(i);

        Shuffle(participants);
        int n = participants.Count;
        int hunters = (n == 2 && forceOneHunterWhenTwo) ? 1 : (n - 1) / 2;

        for (int k = 0; k < n; k++)
        {
            int slotIdx = participants[k];
            PlayerSlot s = Slots[slotIdx];
            bool hunter = k < hunters;
            NetworkCarController.PlayerTeam team = hunter
                ? NetworkCarController.PlayerTeam.Hunter
                : NetworkCarController.PlayerTeam.Defender;

            GetSpawn(k, out Vector3 pos, out Quaternion rot);
            NetworkObject car = Runner.Spawn(launcher.playerPrefab, pos, rot, s.Player,
                (runner, obj) => { obj.GetComponent<NetworkCarController>().MyTeam = team; });
            cars[s.Player] = car;

            s.InMatch = true;
            s.Ready = false;
            s.IsHunter = hunter;
            Slots.Set(slotIdx, s);
        }

        Vector3 cargoPos = launcher.cargoSpawnPoint != null ? launcher.cargoSpawnPoint.position : new Vector3(0, 2, 20);
        cargoObj = Runner.Spawn(launcher.cargoPrefab, cargoPos, Quaternion.identity);

        matchStartTick = Runner.Tick.Raw;
        finishHint = 0;
        trackWarned = false;
        MatchStats.Reset();

        State = MatchState.Playing;
        if (towManager != null) towManager.ServerOnMatchStart();

        Debug.Log($"[MATCH] Bắt đầu: {n} người, {hunters} Hunter, {n - hunters} Defender.");
    }

    // Kết thúc ván: hủy xe + Cargo, về sảnh chờ. C3 sẽ gọi hàm này khi có người thắng.
    public void ServerFinishMatch(NetworkCarController.PlayerTeam winner, WinReason reason)
    {
        if (!Object.HasStateAuthority || State != MatchState.Playing) return;

        MatchSeconds = (Runner.Tick.Raw - matchStartTick) * Runner.DeltaTime;
        WinnerTeam = winner;
        WinReasonCode = (byte)reason;
        BuildResults();   // phải chạy TRƯỚC khi reset Slots bên dưới

        foreach (var kv in cars)
            if (kv.Value != null) Runner.Despawn(kv.Value);
        cars.Clear();

        if (cargoObj != null) Runner.Despawn(cargoObj);
        cargoObj = null;

        for (int i = 0; i < MaxSlots; i++)
        {
            PlayerSlot s = Slots[i];
            if (s.Player.IsNone) continue;
            s.InMatch = false; s.Ready = false; s.IsHunter = false;
            Slots.Set(i, s);
        }

        if (towManager != null) towManager.ServerOnMatchEnd();

        ResultTimer = TickTimer.CreateFromSeconds(Runner, resultSeconds);
        State = MatchState.Result;
        Debug.Log($"[MATCH] Ván kết thúc. Phe thắng: {winner}, lý do: {reason}, {MatchSeconds:0.0}s.");
    }

    void BuildResults()
    {
        var players = new List<PlayerRef>();
        var hunters = new List<bool>();
        for (int i = 0; i < MaxSlots; i++)
        {
            PlayerSlot s = Slots[i];
            if (s.Player.IsNone || !s.InMatch) continue;
            players.Add(s.Player);
            hunters.Add(s.IsHunter);
        }

        PlayerStat[] stats = MatchStats.Build(players, hunters);
        for (int i = 0; i < MaxSlots; i++)
            ResultStats.Set(i, i < stats.Length ? stats[i] : default);

        // MVP = điểm cao nhất trong phe thắng
        bool winnerIsHunter = WinnerTeam == NetworkCarController.PlayerTeam.Hunter;
        float best = -1f;
        PlayerRef mvp = default;
        for (int i = 0; i < stats.Length; i++)
        {
            if (stats[i].IsHunter != winnerIsHunter) continue;
            if (stats[i].Score > best) { best = stats[i].Score; mvp = stats[i].Player; }
        }
        MvpPlayer = mvp;
    }

    void GetSpawn(int k, out Vector3 pos, out Quaternion rot)
    {
        Transform[] pts = launcher.playerSpawnPoints;
        if (pts != null && pts.Length > 0 && pts[k % pts.Length] != null)
        {
            pos = pts[k % pts.Length].position;
            rot = pts[k % pts.Length].rotation;
        }
        else
        {
            pos = new Vector3((k % 4) * 4f, 1f, 0f);
            rot = Quaternion.identity;
        }
    }

    static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}