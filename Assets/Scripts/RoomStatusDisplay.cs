using UnityEngine;
using Fusion;
using TMPro; // 引用 TextMeshPro

// 1. 改为继承 MonoBehaviour (不再是 NetworkBehaviour)
public class RoomStatusDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _statusText;

    // 缓存 Runner 引用，避免每帧 Find
    private NetworkRunner _runner;

    void Update()
    {
        // 2. 懒加载：如果还没找到 Runner，就去找一下
        if (_runner == null)
        {
            _runner = FindAnyObjectByType<NetworkRunner>();
        }

        // 3. 读取数据并显示
        if (_runner != null && _runner.IsRunning && _runner.SessionInfo != null)
        {
            int count = _runner.SessionInfo.PlayerCount;
            int max = _runner.SessionInfo.MaxPlayers;

            // 区分一下本机是 Host 还是 Client
            string role = _runner.IsServer ? "HOST" : "CLIENT";

            _statusText.text = $"[{role}] Room: {_runner.SessionInfo.Name}\nPlayers: {count}/{max}";
        }
        else
        {
            _statusText.text = "Connecting...";
        }
    }
}