using UnityEngine;
using Fusion;
using System.Collections.Generic;

public class AquariumManager : SimulationBehaviour, IPlayerJoined
{
    // [新增] 静态列表，存活的所有鱼都会注册到这里
    public static List<SimpleBoid> AllBoids = new List<SimpleBoid>();

    [Header("Spawning")]
    [Tooltip("三种不同材质的鱼预制件，生成时会随机选择")]
    public NetworkObject[] FishPrefabs = new NetworkObject[3];

    // [新增] 环境系统的预制体槽位
    public NetworkObject EnvironmentPrefab;

    [Tooltip("鱼的总数量，会随机分配到三种鱼上")]
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

        // 2. 生成鱼群（在三种鱼预制件中随机选择，总数由 FishCount 控制）
        if (FishPrefabs == null || FishPrefabs.Length == 0)
        {
            Debug.LogWarning("[Aquarium] 未配置 FishPrefabs，跳过生成鱼。");
            return;
        }
        Debug.Log($"[Aquarium] Spawning {FishCount} fishes (random among {FishPrefabs.Length} prefabs)...");
        for (int i = 0; i < FishCount; i++)
        {
            NetworkObject prefab = FishPrefabs[Random.Range(0, FishPrefabs.Length)];
            if (prefab == null) continue;
            Vector3 randomPos = new Vector3(0, 1.5f, 1) + Random.insideUnitSphere * 4.0f;
            Quaternion randomRot = Random.rotation;
            Runner.Spawn(prefab, randomPos, randomRot, null);
        }
    }
}
