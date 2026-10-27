using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;

public class NetworkLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    private NetworkRunner _runner;

    [Header("Network Prefab")]

    [Tooltip("Kéo một vật thể có NetworkObject vào đây để spawn khi có người vào phòng")]
    public NetworkPrefabRef playerPrefab;
    public NetworkPrefabRef cargoPrefab; // <-- THÊM DÒNG NÀY ĐỂ KÉO PREFAB CARGO

    [Header("Spawn Settings")]
    public Transform[] playerSpawnPoints; // Kéo 4 cái Spawn_1,2,3,4 vào đây
    public Transform cargoSpawnPoint;     // Kéo cái Cargo_Spawn vào đây

    [Header("PvP Test Settings")]
    [Tooltip("Phe mặc định của máy Chủ (Host)")]
    public NetworkCarController.PlayerTeam hostTeam = NetworkCarController.PlayerTeam.Defender;

    public enum TestMode { SameTeam, HostVsClient }
    [Tooltip("Chế độ Test: Cùng phe hay Đối đầu?")]
    public TestMode teamTestMode = TestMode.HostVsClient;

    private void OnGUI()
    {
        if (_runner == null)
        {
            if (GUI.Button(new Rect(20, 20, 220, 40), "LÀM CHỦ PHÒNG (HOST)"))
            {
                StartGame(GameMode.Host);
            }
            if (GUI.Button(new Rect(20, 70, 220, 40), "THAM GIA PHÒNG (CLIENT)"))
            {
                StartGame(GameMode.Client);
            }
        }
        else
        {
            GUI.Label(new Rect(20, 20, 300, 30), $"ĐÃ KẾT NỐI! Chế độ: {_runner.GameMode}");
        }
    }

    async void StartGame(GameMode mode)
    {
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
        var sceneInfo = new NetworkSceneInfo();
        if (scene.IsValid)
        {
            sceneInfo.AddSceneRef(scene, LoadSceneMode.Single);
        }

        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = "ChainDrift_Room",
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        // (ĐÃ XÓA ĐOẠN SPAWN Ở ĐÂY ĐỂ TRÁNH BỊ SCENE NUỐT MẤT)
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            Debug.Log($"Người chơi {player} đã vào phòng! Đang tạo nhân vật...");

            Vector3 spawnPosition;
            Quaternion spawnRotation = Quaternion.identity;
            if (playerSpawnPoints != null && playerSpawnPoints.Length > 0)
            {
                Transform sp = playerSpawnPoints[player.RawEncoded % playerSpawnPoints.Length];
                spawnPosition = sp.position;
                spawnRotation = sp.rotation;
            }
            else
            {
                float xOffset = (player.RawEncoded % 4) * 4f;
                spawnPosition = new Vector3(xOffset, 1, 0);
            }

            // 1. Sinh xe và lưu vào biến carObj
            NetworkObject carObj = runner.Spawn(playerPrefab, spawnPosition, spawnRotation, player);

            // ========================================================
            // 2. LOGIC CHIA PHE (CHỈ SERVER MỚI ĐƯỢC QUYẾT ĐỊNH)
            // ========================================================
            if (carObj != null)
            {
                NetworkCarController carCtrl = carObj.GetComponent<NetworkCarController>();
                if (carCtrl != null)
                {
                    if (player == runner.LocalPlayer)
                    {
                        // Nếu là Host: Lấy phe mặc định
                        carCtrl.MyTeam = hostTeam;
                    }
                    else
                    {
                        // Nếu là Client: Xét theo chế độ Test
                        if (teamTestMode == TestMode.SameTeam)
                        {
                            carCtrl.MyTeam = hostTeam; // Cùng phe Host
                        }
                        else
                        {
                            // Khác phe Host (Đảo ngược)
                            carCtrl.MyTeam = (hostTeam == NetworkCarController.PlayerTeam.Defender)
                                ? NetworkCarController.PlayerTeam.Hunter
                                : NetworkCarController.PlayerTeam.Defender;
                        }
                    }
                }
            }

            // NẾU NGƯỜI VÀO PHÒNG LÀ HOST -> SINH RA KHỐI CARGO
            if (player == runner.LocalPlayer)
            {
                Debug.Log(">> ĐANG SINH RA KHỐI CARGO TRÊN MẠNG! <<");
                Vector3 cargoPos = cargoSpawnPoint != null ? cargoSpawnPoint.position : new Vector3(0, 2, 20);
                runner.Spawn(cargoPrefab, cargoPos, Quaternion.identity);
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Người chơi {player} đã rời phòng!");
    }

    // ====================================================================
    // CÁC HÀM LẮNG NGHE ĐÃ ĐƯỢC CHUẨN HÓA CHO FUSION 2 MỚI NHẤT
    // ====================================================================


    // Hàm này của Fusion chạy liên tục để đóng gói phím bấm của người chơi gửi lên Server
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        // 1. Nhận phím Tiến/Lùi
        if (Input.GetKey(KeyCode.W)) data.moveInput += 1f;
        if (Input.GetKey(KeyCode.S)) data.moveInput -= 1f;

        // 2. Nhận phím Rẽ
        if (Input.GetKey(KeyCode.A)) data.steerInput -= 1f;
        if (Input.GetKey(KeyCode.D)) data.steerInput += 1f;

        // 3. Nhận phím Drift
        data.isDrifting = Input.GetKey(KeyCode.Space);

        // THÊM DÒNG NÀY: Bắt phím F để bắn/tháo xích
        data.isTowPressed = Input.GetKey(KeyCode.F);

        // Đóng gói và đẩy dữ liệu vào đường truyền mạng của Fusion
        input.Set(data);
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    // DÒNG NÀY ĐÃ ĐƯỢC CẬP NHẬT SANG ReadOnlySpan<byte>:
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}