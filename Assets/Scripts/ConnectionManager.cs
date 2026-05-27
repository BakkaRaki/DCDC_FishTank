using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class ConnectionManager : MonoBehaviour
{
    [Header("Fusion References")]
    [SerializeField] private NetworkRunner _runnerPrefab;

    [Header("Scene Settings")]
    // GameScene ?? Build Settings ????????
    private const int GameSceneIndex = 1;

    // ?????????????????
    private bool _isConnecting = false;

    public void OnStartHostClicked()
    {
        StartGame(GameMode.Host);
    }

    public void OnJoinClientClicked()
    {
        StartGame(GameMode.Client);
    }

    private async void StartGame(GameMode mode)
    {
        if (_isConnecting) return; // ??????????????????
        _isConnecting = true;
        AquariumColocationGate.Reset();

        // 1. ??????? Runner (???????????)
        // ???????????????? NetworkRunner ????"??"???????????
        var existingRunner = FindAnyObjectByType<NetworkRunner>();
        if (existingRunner != null)
        {
            Debug.Log("[ConnectionManager] Destroying existing runner...");
            Destroy(existingRunner.gameObject);
        }

        // 2. ???????????? Runner
        // ????? Prefab ????????????????????????
        var runner = Instantiate(_runnerPrefab);

        // 3. ??????????????
        var scene = SceneRef.FromIndex(GameSceneIndex);
        var sceneInfo = new NetworkSceneInfo();
        if (scene.IsValid)
        {
            sceneInfo.AddSceneRef(scene, LoadSceneMode.Additive);
        }

        // 4. ???? Fusion
        try
        {
            Debug.Log($"[ConnectionManager] Starting Fusion as {mode}...");

            await runner.StartGame(new StartGameArgs()
            {
                GameMode = mode,
                SessionName = AquariumSessionConfig.FusionSessionName,
                Scene = scene,
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>()
            });

            Debug.Log($"[ConnectionManager] Success! Started as {mode}.");
            FishTankLog.Info($"Fusion started as {mode}, session={AquariumSessionConfig.FusionSessionName}");
        }
        catch (System.Exception e)
        {
            // ????????????????????????
            Debug.LogError($"[ConnectionManager] Failed to start: {e.Message}");
            _isConnecting = false;
            // ????????????????? runner
            if (runner != null) Destroy(runner.gameObject);
        }
    }
}