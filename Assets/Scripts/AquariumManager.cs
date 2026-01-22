using UnityEngine;
using Fusion;
using System.Collections.Generic;

// 1. 修改继承：从 NetworkBehaviour 改为 SimulationBehaviour
public class AquariumManager : SimulationBehaviour, IPlayerJoined
{
    [Header("Spawning")]
    public NetworkObject FishPrefab;
    public int FishCount = 20;

    private bool _hasSpawned = false;

    // 2. 为了保险起见，我们把生成逻辑放在 PlayerJoined 或者 SceneLoadDone 里
    // 因为 SimulationBehaviour 在 Runner 上时，FixedUpdateNetwork 的执行时机有时比较微妙
    // 这里我们改用 IPlayerJoined 接口，确保有玩家（Host自己）进来了再生成

    public void PlayerJoined(PlayerRef player)
    {
        // 只有 Host 执行，且只执行一次
        if (Runner.IsServer && !_hasSpawned)
        {
            SpawnFishSchool();
            _hasSpawned = true;
        }
    }

    void SpawnFishSchool()
    {
        Debug.Log($"[Aquarium] Spawning {FishCount} fishes...");
        for (int i = 0; i < FishCount; i++)
        {
            Vector3 randomPos = new Vector3(0, 1.5f, 1) + Random.insideUnitSphere * 2.0f;
            Quaternion randomRot = Random.rotation;
            Runner.Spawn(FishPrefab, randomPos, randomRot, null);
        }
    }

    // 如果你之前的 FixedUpdateNetwork 逻辑没问题，也可以保留，但改成 IPlayerJoined 更稳健
}