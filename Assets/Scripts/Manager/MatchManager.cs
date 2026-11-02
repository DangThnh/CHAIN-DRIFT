using System.Collections.Generic;
using UnityEngine;
using Fusion;

public enum MatchState : byte { Lobby = 0, Playing = 1 }

public struct PlayerSlot : INetworkStruct
{
    public PlayerRef Player;
    public NetworkBool Ready;
    public NetworkBool InMatch;   // true = đang có xe trong ván này
    public NetworkBool IsHunter;
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

    [Networked] public MatchState State { get; set; }
    [Networked] public PlayerRef HostPlayer { get; set; }
    [Networked, Capacity(MaxSlots)] private NetworkArray<PlayerSlot> Slots => default;

    // Chỉ dùng trên server
    private readonly Dictionary<PlayerRef, NetworkObject> cars = new Dictionary<PlayerRef, NetworkObject>();
    private NetworkObject cargoObj;
    private readonly List<int> participants = new List<int>();

    public override void Spawned()
    {
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
        if (Runner.Tick.Raw % Mathf.Max(1, rosterSyncTicks) != 0) return;
        SyncRoster();
    }

    public override void Render()
    {
        // TẠM: Host bấm F9 để kết thúc ván
        if (Object.HasStateAuthority && Input.GetKeyDown(debugEndKey)) ServerEndMatch();
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

        State = MatchState.Playing;
        Debug.Log($"[MATCH] Bắt đầu: {n} người, {hunters} Hunter, {n - hunters} Defender.");
    }

    // Kết thúc ván: hủy xe + Cargo, về sảnh chờ. C3 sẽ gọi hàm này khi có người thắng.
    public void ServerEndMatch()
    {
        if (!Object.HasStateAuthority || State != MatchState.Playing) return;

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
        State = MatchState.Lobby;
        Debug.Log("[MATCH] Ván kết thúc, về sảnh chờ.");
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