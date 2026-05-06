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

    // ???????
    private OVRHand _localRightHand;
    private bool _wasPinching = false;
    private bool _wasTriggerPressed = false;
    private float _lastLocalSpawnRealtime = -999f;
    private Transform _cameraTransform;

    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            FindLocalHand();
            // ?????? MainCamera
            if (Camera.main != null) _cameraTransform = Camera.main.transform;
        }
    }

    void FindLocalHand()
    {
        var hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
        foreach (var hand in hands)
        {
            // ???????? PointerPose ??????????????????????
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
        if (!Object.HasInputAuthority) return;

        if (!Runner.IsForward) return;

        if (_localRightHand == null || _localRightHand.PointerPose == null)
        {
            FindLocalHand();
        }

        bool currentPinch = _localRightHand != null &&
                            _localRightHand.IsTracked &&
                            _localRightHand.PointerPose != null &&
                            _localRightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        bool currentTrigger = CheckVRTrigger();

        if (!SpawnCoolDown.ExpiredOrNotRunning(Runner))
        {
            _wasPinching = currentPinch;
            _wasTriggerPressed = currentTrigger;
            return;
        }

        bool shouldSpawn = false;
        Vector3 finalSpawnPos = Vector3.zero; // ?????? 0

        // --- 1. ?????? (Pinch) ---
        if (_localRightHand != null && _localRightHand.IsTracked && _localRightHand.PointerPose != null)
        {
            if (currentPinch && !_wasPinching)
            {
                shouldSpawn = true;
                finalSpawnPos = _localRightHand.PointerPose.position;
                Debug.DrawLine(_localRightHand.PointerPose.position, _localRightHand.PointerPose.position + Vector3.up * 0.2f, Color.green, 2.0f);
            }
        }

        // --- 2. ???/???/Avatar ?????? ---
        // ?????????????????????????
        if (!shouldSpawn)
        {
            bool mouse = Input.GetMouseButtonDown(0);

            if ((currentTrigger && !_wasTriggerPressed) || mouse)
            {
                shouldSpawn = true;
                // ??????????????????¦Ë??
                if (_cameraTransform != null)
                {
                    finalSpawnPos = _cameraTransform.position + _cameraTransform.forward * 0.5f;
                }
                // ???????? Avatar ????¦Ë?? (????????????)
                else
                {
                    finalSpawnPos = transform.position + transform.forward * 0.5f;
                }
            }
        }

        // --- 3. ??????? (????????????????) ---
        if (shouldSpawn)
        {
            if (Time.realtimeSinceStartup - _lastLocalSpawnRealtime < 0.35f)
            {
                _wasPinching = currentPinch;
                _wasTriggerPressed = currentTrigger;
                return;
            }

            // ??? finalSpawnPos ???? (0,0,0)?????????????????
            if (finalSpawnPos == Vector3.zero)
            {
                Debug.LogWarning("[PlayerActions] ?????¦Ë???? 0?????????????????????");
                if (_cameraTransform != null)
                    finalSpawnPos = _cameraTransform.position + _cameraTransform.forward * 0.5f;
                else
                {
                    _wasPinching = currentPinch;
                    _wasTriggerPressed = currentTrigger;
                    return; // ???????????§µ???????????????????
                }
            }

            // ????????????
            _lastLocalSpawnRealtime = Time.realtimeSinceStartup;
            RPC_SpawnFood(finalSpawnPos);

            // ???????
            SpawnCoolDown = TickTimer.CreateFromSeconds(Runner, CooldownTime);
        }

        _wasPinching = currentPinch;
        _wasTriggerPressed = currentTrigger;
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
        pos += Vector3.down * 0.03f;

        if (Meta.XR.MRUtilityKit.MRUK.Instance != null)
        {
            var room = Meta.XR.MRUtilityKit.MRUK.Instance.GetCurrentRoom();
            if (room != null && !room.IsPositionInRoom(pos, true))
            {
                if (MRSurfaceAvoidance.TryGetClosestSurface(pos, out Vector3 surfPos, out Vector3 normal))
                {
                    pos = surfPos + normal * 0.05f;
                    Debug.Log("[PlayerActions] Food position snapped to nearest real surface (was out of room).");
                }
            }
        }

        Runner.Spawn(FoodPrefab, pos, Quaternion.identity);
    }
}