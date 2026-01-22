using UnityEngine;
using Fusion;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class ConnectionManager : MonoBehaviour
{
    [Header("Fusion References")]
    [SerializeField] private NetworkRunner _runnerPrefab;

    [Header("Scene Settings")]
    // GameScene 在 Build Settings 中的索引
    private const int GameSceneIndex = 1;

    // 防止连点按钮造成多次启动
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
        if (_isConnecting) return; // 如果正在连接，忽略点击
        _isConnecting = true;

        // 1. 清理旧的 Runner (关键修改！！！)
        // 任何遗留在场景里的 NetworkRunner 都是"脏"的，必须销毁
        var existingRunner = FindAnyObjectByType<NetworkRunner>();
        if (existingRunner != null)
        {
            Debug.Log("[ConnectionManager] Destroying existing runner...");
            Destroy(existingRunner.gameObject);
        }

        // 2. 实例化一个全新的 Runner
        // 必须从 Prefab 生成一个新的，确保它是干净的状态
        var runner = Instantiate(_runnerPrefab);

        // 3. 准备场景加载参数
        var scene = SceneRef.FromIndex(GameSceneIndex);
        var sceneInfo = new NetworkSceneInfo();
        if (scene.IsValid)
        {
            sceneInfo.AddSceneRef(scene, LoadSceneMode.Additive);
        }

        // 4. 启动 Fusion
        try
        {
            Debug.Log($"[ConnectionManager] Starting Fusion as {mode}...");

            await runner.StartGame(new StartGameArgs()
            {
                GameMode = mode,
                SessionName = "AquariumRoom",
                Scene = scene,
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>()
            });

            Debug.Log($"[ConnectionManager] Success! Started as {mode}.");
        }
        catch (System.Exception e)
        {
            // 如果连接失败，重置状态以便重试
            Debug.LogError($"[ConnectionManager] Failed to start: {e.Message}");
            _isConnecting = false;
            // 失败时也销毁这个废掉的 runner
            if (runner != null) Destroy(runner.gameObject);
        }
    }
}