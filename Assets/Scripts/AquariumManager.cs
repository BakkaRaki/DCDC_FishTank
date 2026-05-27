using UnityEngine;
using Fusion;
using System.Collections;
using System.Collections.Generic;

public class AquariumManager : SimulationBehaviour, IPlayerJoined
{
    public static List<SimpleBoid> AllBoids = new List<SimpleBoid>();

    [Header("Spawning")]
    [Tooltip("????????????????????????????????")]
    public NetworkObject[] FishPrefabs = new NetworkObject[3];

    public NetworkObject EnvironmentPrefab;

    [Tooltip("????????????????????????????")]
    public int FishCount = 30;

    private bool _hasSpawned = false;

    public void PlayerJoined(PlayerRef player)
    {
        if (Runner.IsServer && !_hasSpawned)
        {
            FishTankLog.Info($"Aquarium: PlayerJoined {player}, starting spawn waiter");
            StartCoroutine(SpawnWhenColocationReady());
        }
    }

    IEnumerator SpawnWhenColocationReady()
    {
        FishTankLog.Info("Aquarium: waiting for Colocation gate??");
        while (!AquariumColocationGate.IsReady)
            yield return null;

        if (_hasSpawned) yield break;
        FishTankLog.Info("Aquarium: Colocation ready ?? spawning content");
        SpawnContent();
        _hasSpawned = true;
    }

    void SpawnContent()
    {
        if (EnvironmentPrefab != null)
        {
            Runner.Spawn(EnvironmentPrefab, Vector3.zero, Quaternion.identity);
            Debug.Log("[Aquarium] Environment System Spawned.");
        }

        if (FishPrefabs == null || FishPrefabs.Length == 0)
        {
            Debug.LogWarning("[Aquarium] ?????? FishPrefabs????????????");
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
