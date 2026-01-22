using UnityEngine;
using Fusion;
using TMPro; // 记得引用 TextMeshPro

public class RoomStatusDisplay : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI _statusText;

    public override void FixedUpdateNetwork()
    {
        // 只有当网络在运行时才更新
        if (Runner != null && Runner.IsRunning)
        {
            int count = Runner.SessionInfo.PlayerCount;
            string mode = Runner.GameMode.ToString();

            _statusText.text = $"Mode: {mode}\nPlayers: {count}/4\nMy ID: {Runner.LocalPlayer}";
        }
        else
        {
            _statusText.text = "Offline";
        }
    }
}