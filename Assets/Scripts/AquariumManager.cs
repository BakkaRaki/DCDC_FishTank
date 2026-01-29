using UnityEngine;
using Fusion;
using System.Collections.Generic;

public class AquariumManager : SimulationBehaviour, IPlayerJoined
{
    // [新增] 静态列表，存活的所有鱼都会注册到这里
    public static List<SimpleBoid> AllBoids = new List<SimpleBoid>();

    [Header("Spawning")]
    public NetworkObject FishPrefab;
    public int FishCount = 20;

    private bool _hasSpawned = false;

    public void PlayerJoined(PlayerRef player)
    {
        if (Runner.IsServer && !_hasSpawned)
        {
            SpawnFishSchool();
            _hasSpawned = true;
        }
    }

    void SpawnFishSchool()
    {
        AllBoids.Clear();
        Debug.Log($"[Aquarium] Spawning {FishCount} fishes...");

        for (int i = 0; i < FishCount; i++)
        {
            // [修改] 扩大生成半径到 4.0f，让它们散布在整个房间
            Vector3 randomPos = new Vector3(0, 1.5f, 1) + Random.insideUnitSphere * 4.0f;

            // [可选] 也可以分成两拨生成
            if (i % 2 == 0) randomPos += Vector3.right * 2;
            else randomPos -= Vector3.right * 2;

            Quaternion randomRot = Random.rotation;
            Runner.Spawn(FishPrefab, randomPos, randomRot, null);
        }
    }
}