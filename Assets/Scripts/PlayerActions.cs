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
    private Transform _cameraTransform;

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            FindLocalHand();
            // 务必找到 MainCamera
            if (Camera.main != null) _cameraTransform = Camera.main.transform;
        }
    }

    void FindLocalHand()
    {
        var hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
        foreach (var hand in hands)
        {
            // 修复：确保 PointerPose 不为空再赋值，否则赋值了也没用
            if (hand.PointerPose != null && (hand.name.Contains("Right") || hand.PointerPose.name.Contains("Right")))
            {
                _localRightHand = hand;
                Debug.Log($"[PlayerActions] Found Right Hand: {hand.name}");
                break;
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 只有本地玩家能操作
        if (!Object.HasInputAuthority) return;

        // 冷却检测
        if (!SpawnCoolDown.ExpiredOrNotRunning(Runner)) return;

        bool shouldSpawn = false;
        Vector3 finalSpawnPos = Vector3.zero; // 初始化为 0

        // --- 1. 手势检测 (Pinch) ---
        if (_localRightHand != null && _localRightHand.IsTracked && _localRightHand.PointerPose != null)
        {
            bool isPinching = _localRightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);

            if (isPinching && !_wasPinching)
            {
                shouldSpawn = true;
                finalSpawnPos = _localRightHand.PointerPose.position;
                Debug.DrawLine(_localRightHand.PointerPose.position, _localRightHand.PointerPose.position + Vector3.up * 0.2f, Color.green, 2.0f);
            }
            _wasPinching = isPinching;
        }

        // --- 2. 手柄/鼠标/Avatar 回退检测 ---
        // 如果手势没触发，检查其他输入
        if (!shouldSpawn)
        {
            bool trigger = CheckVRTrigger();
            bool mouse = Input.GetMouseButtonDown(0);

            if (trigger || mouse)
            {
                shouldSpawn = true;
                // 优先用摄像机（眼睛）位置
                if (_cameraTransform != null)
                {
                    finalSpawnPos = _cameraTransform.position + _cameraTransform.forward * 0.5f;
                }
                // 实在不行用 Avatar 身体位置 (不推荐，容易偏)
                else
                {
                    finalSpawnPos = transform.position + transform.forward * 0.5f;
                }
            }
        }

        // --- 3. 终极修正 (防止生成在世界原点) ---
        if (shouldSpawn)
        {
            // 如果 finalSpawnPos 还是 (0,0,0)，说明上面的获取失败了
            if (finalSpawnPos == Vector3.zero)
            {
                Debug.LogWarning("[PlayerActions] 算出的位置是 0！强制修正到摄像机前方！");
                if (_cameraTransform != null)
                    finalSpawnPos = _cameraTransform.position + _cameraTransform.forward * 0.5f;
                else
                    return; // 连摄像机都没有，那就不生成了，免得去原点
            }

            // 发送生成请求
            RPC_SpawnFood(finalSpawnPos);

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
            return val;
        }
        return false;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SpawnFood(Vector3 pos)
    {
        // Host 收到位置，生成物体
        Runner.Spawn(FoodPrefab, pos, Quaternion.identity);
    }
}