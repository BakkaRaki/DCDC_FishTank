using UnityEngine;
using Fusion;
using System.Collections.Generic;

public class AquariumManager : SimulationBehaviour, IPlayerJoined
{
    // [新增] 静态列表，存活的所有鱼都会注册到这里
    public static List<SimpleBoid> AllBoids = new List<SimpleBoid>();

    [Header("Spawning")]
    public NetworkObject FishPrefab;

    // [新增] 环境系统的预制体槽位
    public NetworkObject EnvironmentPrefab;

    public int FishCount = 20;

    private bool _hasSpawned = false;

    public void PlayerJoined(PlayerRef player)
    {
        if (Runner.IsServer && !_hasSpawned)
        {
            SpawnContent(); // 改个名，统一管理
            _hasSpawned = true;
        }
    }

    void SpawnContent()
    {
        // 1. 生成环境系统 (只生成 1 个)
        if (EnvironmentPrefab != null)
        {
            Runner.Spawn(EnvironmentPrefab, Vector3.zero, Quaternion.identity);
            Debug.Log("[Aquarium] Environment System Spawned.");
        }

        // 2. 生成鱼群
        Debug.Log($"[Aquarium] Spawning {FishCount} fishes...");
        for (int i = 0; i < FishCount; i++)
        {
            Vector3 randomPos = new Vector3(0, 1.5f, 1) + Random.insideUnitSphere * 4.0f;
            Quaternion randomRot = Random.rotation;
            Runner.Spawn(FishPrefab, randomPos, randomRot, null);
        }
    }
}
