using UnityEngine;
using Fusion;
using System.Collections.Generic;
using UnityEngine.XR;

public class PlayerActions : NetworkBehaviour
{
    [Header("Settings")]
    public NetworkObject FoodPrefab;
    public float CooldownTime = 0.5f;

    [Networked] private TickTimer SpawnCoolDown { get; set; }

    // 本地缓存
    private OVRHand _localRightHand;
    private bool _wasPinching = false;

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            FindLocalHand();
        }
    }

    void FindLocalHand()
    {
        // 尝试寻找右手的 OVRHand 组件
        var hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
        foreach (var hand in hands)
        {
            // 依然保持大写 P 的修复
            if (hand.PointerPose != null && (hand.name.Contains("Right") || hand.PointerPose.name.Contains("Right")))
            {
                _localRightHand = hand;
                break;
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 只有本地玩家能发起操作
        if (!Object.HasInputAuthority) return;

        // 如果处于冷却中，直接跳过检测，节省性能
        if (!SpawnCoolDown.ExpiredOrNotRunning(Runner)) return;

        bool shouldSpawn = false;
        Vector3 spawnPos = Vector3.zero;

        // 1. 优先检测手势 (Quest 3 原生体验)
        if (_localRightHand != null && _localRightHand.PointerPose != null)
        {
            bool isPinching = _localRightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

            // 逻辑：按下的一瞬间触发 (Down)
            if (isPinching && !_wasPinching)
            {
                shouldSpawn = true;
                spawnPos = _localRightHand.PointerPose.position; // 指尖位置
            }
            _wasPinching = isPinching;
        }
        // 2. 其次检测手柄 (兼容模式)
        else
        {
            if (CheckVRTrigger())
            {
                shouldSpawn = true;
                // 手柄通常在 Avatar 手部位置前方
                spawnPos = transform.position + transform.forward * 0.3f;
            }
        }

        // 3. 最后检测鼠标 (PC 调试专用)
        // 注意：Input.GetMouseButton 需要 Project Settings -> Player -> Active Input Handling 设为 Both
        if (!shouldSpawn && Input.GetMouseButtonDown(0))
        {
            shouldSpawn = true;
            spawnPos = transform.position + transform.forward * 0.5f;
        }

        // 执行生成
        if (shouldSpawn && FoodPrefab != null)
        {
            RPC_SpawnFood(spawnPos);
            // 重置冷却
            SpawnCoolDown = TickTimer.CreateFromSeconds(Runner, CooldownTime);
        }
    }

    private bool CheckVRTrigger()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);
        if (devices.Count > 0)
        {
            devices[0].TryGetFeatureValue(CommonUsages.triggerButton, out bool val);
            // 这里用简单判断，如果是持续按下，依靠 Cooldown 来限制频率
            return val;
        }
        return false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SpawnFood(Vector3 pos)
    {
        Runner.Spawn(FoodPrefab, pos, Quaternion.identity);
    }
}