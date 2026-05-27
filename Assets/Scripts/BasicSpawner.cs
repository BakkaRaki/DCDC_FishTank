using UnityEngine;
using Fusion;

// 这个脚本负责监听网络事件，并在玩家加入时生成一个物体
public class BasicSpawner : SimulationBehaviour, IPlayerJoined
{
    // 在 Inspector 里拖入一个简单的 Prefab（比如一个黄色球体）
    // 这个 Prefab 必须挂载 NetworkObject 组件！
    public GameObject PlayerPrefab;

    public void PlayerJoined(PlayerRef player)
    {
        // 只有 Host (主机) 有权限生成物体
        if (Runner.IsServer)
        {
            Debug.Log($"[Spawner] Requesting Spawn for Player {player}...");

            // 在 (0,1,0) 的位置生成玩家，稍微错开一点避免重叠
            Vector3 spawnPosition = new Vector3(0, 1, 0) + Random.insideUnitSphere * 0.5f;

            // Fusion 的生成命令（每个 PlayerRef 只能生成一次，否则会导致 InputAuthority/RPC 混乱）
            var obj = Runner.Spawn(PlayerPrefab, spawnPosition, Quaternion.identity, player);
            if (obj != null) Debug.Log($"[Spawner] SUCCESS! Object Spawned: {obj.Id}");
            else Debug.LogError("[Spawner] FAILED! Runner.Spawn returned null.");
        }
    }
}