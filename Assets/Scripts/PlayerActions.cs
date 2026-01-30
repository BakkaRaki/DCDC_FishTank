using UnityEngine;
using Fusion;
using System.Collections.Generic;
using UnityEngine.XR;

public class PlayerActions : NetworkBehaviour
{
    public NetworkObject FoodPrefab;
    [Networked] private TickTimer SpawnCoolDown { get; set; }

    // 引用
    private OVRHand _localRightHand;

    // 调试开关
    private bool _hasLoggedMissingPrefab = false;

    public override void Spawned()
    {
        // 1. 检查是否是本地玩家
        if (Object.HasInputAuthority)
        {
            Debug.Log($"[调试] 玩家对象生成成功。我是本地玩家吗？Yes。ID: {Object.Id}");
            FindLocalHand();
        }
        else
        {
            // 如果日志里只出现这句，没出现上面的 Yes，说明 Authority 设置有问题
            // 但通常 Client 上应该至少有一条日志是 Yes
        }
    }

    void FindLocalHand()
    {
        // 尝试找 OVRHand
        OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
        foreach (var hand in hands)
        {
            if (hand.PointerPose != null && (hand.name.Contains("Right") || hand.PointerPose.name.Contains("Right")))
            {
                _localRightHand = hand;
                Debug.Log($"[调试] 成功找到 OVRHand 右手组件: {hand.name}");
                break;
            }
        }

        if (_localRightHand == null)
        {
            Debug.LogWarning("[调试] 警告：未找到 OVRHand 组件！手势追踪将无法使用。将尝试回退到手柄/鼠标。");
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 只有本地玩家执行检测
        if (!Object.HasInputAuthority) return;

        // 2. 检查 Prefab 是否赋值 (这是最常见的错误！)
        if (FoodPrefab == null)
        {
            if (!_hasLoggedMissingPrefab)
            {
                Debug.LogError("[调试] 严重错误：FoodPrefab 没赋值！请在 Inspector 里拖入 NetworkFood！");
                _hasLoggedMissingPrefab = true;
            }
            return;
        }

        // 3. 检测输入
        bool inputActive = false;
        string inputSource = "";

        // A. 检测手势 (Pinch)
        if (_localRightHand != null)
        {
            if (_localRightHand.GetFingerIsPinching(OVRHand.HandFinger.Index))
            {
                inputActive = true;
                inputSource = "手势捏合";
            }
        }

        // B. 检测手柄 (Trigger) - 通用 XR 方法
        if (!inputActive)
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);
            if (devices.Count > 0)
            {
                devices[0].TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerValue);
                if (triggerValue)
                {
                    inputActive = true;
                    inputSource = "手柄扳机";
                }
            }
        }

        // C. 检测鼠标 (PC调试用)
        if (!inputActive && Input.GetMouseButton(0))
        {
            inputActive = true;
            inputSource = "鼠标左键";
        }

        // 4. 执行生成
        if (inputActive)
        {
            if (SpawnCoolDown.ExpiredOrNotRunning(Runner))
            {
                Debug.Log($"[调试] 检测到 {inputSource}！正在请求 RPC 生成食物...");

                // 计算位置
                Vector3 spawnPos = transform.position + transform.forward * 0.5f;
                if (_localRightHand != null)
                    spawnPos = _localRightHand.PointerPose.position;

                RPC_RequestSpawnFood(spawnPos);

                // 重置冷却 (本地稍微设置一下防止发太多 Log)
                SpawnCoolDown = TickTimer.CreateFromSeconds(Runner, 0.5f);
            }
            else
            {
                // 冷却中，不打印日志，不然会刷屏
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestSpawnFood(Vector3 spawnPos)
    {
        Debug.Log("[调试] Host 收到 RPC 请求！正在生成 NetworkFood...");
        Runner.Spawn(FoodPrefab, spawnPos, Quaternion.identity);
    }
}