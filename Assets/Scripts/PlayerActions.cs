using System;
using UnityEngine;
using Fusion;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.XR;
using Meta.XR.MRUtilityKit;

public class PlayerActions : NetworkBehaviour
{
    [Header("Settings")]
    public NetworkObject FoodPrefab;
    public float CooldownTime = 0.5f;

    [Networked] private TickTimer SpawnCoolDown { get; set; }

    // ???????
    private OVRHand _localRightHand;
    private Transform _rightIndexTip;
    /// <summary>Max distance from PointerPose for an index-tip transform to be trusted (meters).</summary>
    private const float MaxIndexTipPointerDistance = 0.15f;
    private bool _wasPinching = false;
    private bool _wasPinchingRender = false;
    private bool _pinchSpawnPending;
    private Vector3 _pinchSpawnPos;
    private string _pinchSpawnSource = "unknown";
    private float _pinchSpawnRealtime = -999f;
    private bool _wasTriggerPressed = false;
    private float _lastLocalSpawnRealtime = -999f;
    private Transform _cameraTransform;
    private int _rightHandMissingFrames = 0;
    private const int RightHandReacquireFrames = 30;

    /// <summary>Fusion can invoke FixedUpdateNetwork more than once per tick for the local player; instance fields won't dedupe RPCs.</summary>
    private static int s_spawnConsumedTick = -1;
    private static PlayerRef s_spawnConsumedPlayer;

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
        if (hands == null || hands.Length == 0) return;

        // 1) Prefer name/prefab hints first (most reliable across tracking origin changes).
        OVRHand nameHintRight = null;
        foreach (var hand in hands)
        {
            if (hand == null || hand.PointerPose == null) continue;
            string hn = hand.name ?? "";
            string pn = hand.PointerPose.name ?? "";
            if (hn.Contains("Right") || hn.Contains("_R") || hn.Contains("HandRight") ||
                pn.Contains("Right") || pn.Contains("_R") || pn.Contains("HandRight"))
            {
                nameHintRight = hand;
                break;
            }
        }

        if (nameHintRight != null)
        {
            _localRightHand = nameHintRight;
        }
        else
        {
            // 2) Fallback: choose the hand physically on the user's right side.
        var cam = _cameraTransform != null ? _cameraTransform : (Camera.main != null ? Camera.main.transform : null);
        if (cam == null)
        {
            foreach (var hand in hands)
            {
                if (hand != null && hand.PointerPose != null)
                {
                    _localRightHand = hand;
                    break;
                }
            }
        }
        else
        {
            float bestScore = float.NegativeInfinity;
            OVRHand best = null;
            foreach (var hand in hands)
            {
                if (hand == null || hand.PointerPose == null) continue;
                Vector3 toHand = hand.PointerPose.position - cam.position;
                float score = Vector3.Dot(cam.right, toHand.normalized);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = hand;
                }
            }
            _localRightHand = best;
        }
        }

        _rightIndexTip = _localRightHand != null ? ResolveRightIndexTip(_localRightHand) : null;

        _rightHandMissingFrames = 0;
    }

    void Update()
    {
        if (!Object.HasInputAuthority) return;

        if (_localRightHand == null || !_localRightHand.IsTracked || _localRightHand.PointerPose == null)
        {
            _wasPinchingRender = false;
            return;
        }

        bool pinchNow = _localRightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        if (pinchNow && !_wasPinchingRender)
        {
            _rightIndexTip = ResolveRightIndexTip(_localRightHand);
            if (TryGetRightHandPinchWorldPosition(_localRightHand, _rightIndexTip, out Vector3 pos, out string src))
            {
                _pinchSpawnPos = pos;
                _pinchSpawnSource = src;
                _pinchSpawnPending = true;
                _pinchSpawnRealtime = Time.realtimeSinceStartup;
            }
        }

        if (_pinchSpawnPending && Time.realtimeSinceStartup - _pinchSpawnRealtime > 0.2f)
            _pinchSpawnPending = false;

        _wasPinchingRender = pinchNow;
    }

    /// <summary>Pinch point in world space; falls back to index tip / estimated pointer pinch.</summary>
    static bool TryGetRightHandPinchWorldPosition(OVRHand hand, Transform indexTip, out Vector3 worldPos, out string source)
    {
        worldPos = default;
        source = "none";
        if (hand == null) return false;

        if (TryGetInteractionIndexTip(hand, out worldPos))
        {
            source = "interactionJoint";
            return true;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var pinchMethod in typeof(OVRHand).GetMethods(flags))
        {
            if (pinchMethod.Name != "GetFingerPinchPosition") continue;
            var ps = pinchMethod.GetParameters();
            if (ps.Length != 2) continue;

            try
            {
                object[] args = { OVRHand.HandFinger.Index, Vector3.zero };
                if (pinchMethod.Invoke(hand, args) is bool ok && ok)
                {
                    worldPos = (Vector3)args[1];
                    source = "pinchReflect";
                    return true;
                }
            }
            catch { /* try next overload */ }
        }

        if (hand.PointerPose != null && hand.IsTracked)
        {
            if (IsTrustedIndexTip(hand, indexTip))
            {
                worldPos = indexTip.position;
                source = "indexTip";
                return true;
            }

            worldPos = EstimatePinchFromPointer(hand.PointerPose);
            source = "pointerEst";
            return true;
        }

        if (indexTip != null && IsDescendantOf(indexTip, hand.transform))
        {
            worldPos = indexTip.position;
            source = "indexTip";
            return true;
        }

        if (hand.PointerPose != null)
        {
            worldPos = hand.PointerPose.position;
            source = "pointerRaw";
            return true;
        }

        return false;
    }

    static bool IsTrustedIndexTip(OVRHand hand, Transform indexTip)
    {
        if (hand == null || indexTip == null || hand.PointerPose == null) return false;
        if (!IsDescendantOf(indexTip, hand.transform)) return false;

        float dist = Vector3.Distance(indexTip.position, hand.PointerPose.position);
        return dist <= MaxIndexTipPointerDistance;
    }

    static bool IsDescendantOf(Transform node, Transform ancestor)
    {
        if (node == null || ancestor == null) return false;
        Transform t = node;
        while (t != null)
        {
            if (t == ancestor) return true;
            t = t.parent;
        }

        return false;
    }

    static Vector3 EstimatePinchFromPointer(Transform pointer)
    {
        return pointer.position + pointer.forward * 0.04f - pointer.up * 0.01f + pointer.right * 0.006f;
    }

    static bool TryGetInteractionIndexTip(OVRHand hand, out Vector3 worldPos)
    {
        worldPos = default;
        if (hand == null) return false;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var mb in hand.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb is OVRHand) continue;
            Type type = mb.GetType();
            string typeName = type.FullName ?? type.Name;
            if (typeName.IndexOf("Interaction", StringComparison.OrdinalIgnoreCase) < 0 &&
                typeName.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            foreach (var method in type.GetMethods(flags))
            {
                if (method.Name != "GetJointPose" && method.Name != "GetPointerPose") continue;
                var ps = method.GetParameters();
                if (ps.Length != 2) continue;

                try
                {
                    if (method.Name == "GetPointerPose")
                    {
                        if (ps.Length == 0)
                        {
                            if (method.Invoke(mb, null) is Pose pose0)
                            {
                                worldPos = pose0.position;
                                return true;
                            }
                        }
                        else if (ps[1].ParameterType.Name.Contains("Pose"))
                        {
                            object[] args = { null, default(Pose) };
                            if (method.Invoke(mb, args) is bool okPtr && okPtr && args[1] is Pose ptrPose)
                            {
                                worldPos = ptrPose.position;
                                return true;
                            }
                        }

                        continue;
                    }

                    if (!ps[0].ParameterType.IsEnum || !ps[1].ParameterType.Name.Contains("Pose")) continue;

                    object jointId = ResolveIndexTipJointId(ps[0].ParameterType);
                    if (jointId == null) continue;

                    object[] argsJoint = { jointId, default(Pose) };
                    if (method.Invoke(mb, argsJoint) is bool okJoint && okJoint && argsJoint[1] is Pose jointPose)
                    {
                        worldPos = jointPose.position;
                        return true;
                    }
                }
                catch { /* SDK mismatch */ }
            }
        }

        return false;
    }

    static object ResolveIndexTipJointId(Type enumType)
    {
        foreach (string name in new[]
                 {
                     "HandIndexTip", "IndexTip", "Index3", "HandIndex3", "Index_End", "IndexDistal",
                     "XRHand_IndexTip", "HandIndex3Tip"
                 })
        {
            try { return Enum.Parse(enumType, name); }
            catch { /* try next */ }
        }

        foreach (var value in Enum.GetValues(enumType))
        {
            string s = value.ToString();
            if (s.IndexOf("Index", StringComparison.OrdinalIgnoreCase) >= 0 &&
                s.IndexOf("Tip", StringComparison.OrdinalIgnoreCase) >= 0)
                return value;
        }

        return null;
    }

    static int GetRealWorldLayerMask()
    {
        int layer = LayerMask.NameToLayer("RealWorld");
        return layer >= 0 ? (1 << layer) : Physics.DefaultRaycastLayers;
    }

    static Vector3 PlaceFoodOnPhysicsSurface(Vector3 pos)
    {
        // Keep spawn at pinch/fingertip; fall simulation handles surfaces after spawn.
        int mask = GetRealWorldLayerMask();
        const float probeRadius = 0.03f;

        Collider[] overlaps = Physics.OverlapSphere(pos, 0.06f, mask, QueryTriggerInteraction.Ignore);
        foreach (var col in overlaps)
        {
            if (col == null) continue;
            Vector3 closest = col.ClosestPoint(pos);
            Vector3 push = pos - closest;
            float d = push.magnitude;
            if (d < probeRadius)
            {
                if (d < 1e-5f) push = Vector3.up;
                else push /= d;
                pos += push * (probeRadius - d + 0.01f);
            }
        }

        return pos;
    }

    static Transform ResolveRightIndexTip(OVRHand hand)
    {
        if (hand == null) return null;

        const BindingFlags memberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // OVRSkeleton often sits above/beside OVRHand (e.g. under OVRCameraRig), not only under the hand GO.
        foreach (var skelMb in EnumerateOvrskeletonsNearHand(hand))
        {
            PropertyInfo bonesProp = skelMb.GetType().GetProperty("Bones", memberFlags);
            if (bonesProp == null || bonesProp.GetValue(skelMb) is not IEnumerable bonesEnum)
                continue;

            foreach (object boneObj in bonesEnum)
            {
                Transform t = ReadBoneTransform(boneObj, memberFlags);
                if (t == null) continue;

                string idName = ReadBoneIdString(boneObj, memberFlags);
                if (IsLikelyIndexFingerTipBoneName(idName))
                    return t;
            }
        }

        return ResolveRightIndexTipByTransformNames(hand);
    }

    static IEnumerable<MonoBehaviour> EnumerateOvrskeletonsNearHand(OVRHand hand)
    {
        var seen = new HashSet<int>();
        Transform node = hand.transform;
        for (int depth = 0; depth < 10 && node != null; depth++, node = node.parent)
        {
            foreach (var mb in node.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "OVRSkeleton") continue;
                if (!seen.Add(mb.GetInstanceID())) continue;
                yield return mb;
            }
        }
    }

    static Transform ReadBoneTransform(object boneObj, BindingFlags memberFlags)
    {
        if (boneObj == null) return null;
        Type bt = boneObj.GetType();
        foreach (var name in new[] { "Transform", "BoneTransform" })
        {
            PropertyInfo p = bt.GetProperty(name, memberFlags);
            if (p != null && p.GetValue(boneObj) is Transform tp)
                return tp;
            FieldInfo f = bt.GetField(name, memberFlags);
            if (f != null && f.GetValue(boneObj) is Transform tf)
                return tf;
        }

        return null;
    }

    static string ReadBoneIdString(object boneObj, BindingFlags memberFlags)
    {
        if (boneObj == null) return null;
        Type bt = boneObj.GetType();
        foreach (var name in new[] { "Id", "BoneId" })
        {
            PropertyInfo p = bt.GetProperty(name, memberFlags);
            if (p != null)
                return p.GetValue(boneObj)?.ToString();
            FieldInfo f = bt.GetField(name, memberFlags);
            if (f != null)
                return f.GetValue(boneObj)?.ToString();
        }

        return null;
    }

    static bool IsLikelyIndexFingerTipBoneName(string idName)
    {
        if (string.IsNullOrEmpty(idName)) return false;
        if (idName.IndexOf("thumb", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

        return idName.IndexOf("index", System.StringComparison.OrdinalIgnoreCase) >= 0
               && idName.IndexOf("tip", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static Transform ResolveRightIndexTipByTransformNames(OVRHand hand)
    {
        Transform fallback = null;
        foreach (var t in hand.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || string.IsNullOrEmpty(t.name)) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("thumb")) continue;

            if (n.Contains("handindexfingertip"))
                continue;

            if (n.Contains("indextip") || n.Contains("index_tip"))
                fallback ??= t;

            if (n.Contains("index") && (n.Contains("tip") || n.Contains("fingertip") || n.Contains("distal") ||
                                        n.Contains("end") || n.Contains("index3") || n.EndsWith("_3")))
                fallback ??= t;
        }

        return fallback;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority) return;

        if (!Runner.IsForward) return;

        bool rightHandValid = _localRightHand != null && _localRightHand.PointerPose != null;
        if (!rightHandValid)
        {
            _rightHandMissingFrames++;
            if (_rightHandMissingFrames >= RightHandReacquireFrames)
            {
                FindLocalHand();
            }
        }
        else
        {
            _rightHandMissingFrames = 0;
            _rightIndexTip = ResolveRightIndexTip(_localRightHand);
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
        Vector3 finalSpawnPos = Vector3.zero;
        string spawnSource = "unknown";

        // Pinch edge captured in Update() for freshest tracking pose (before physics step).
        if (_pinchSpawnPending && Time.realtimeSinceStartup - _pinchSpawnRealtime <= 0.15f)
        {
            shouldSpawn = true;
            finalSpawnPos = _pinchSpawnPos;
            spawnSource = _pinchSpawnSource;
            _pinchSpawnPending = false;
            Debug.DrawLine(finalSpawnPos, finalSpawnPos + Vector3.up * 0.2f, Color.green, 2.0f);
        }

        // --- 2. ???/???/Avatar ?????? ---
        // ?????????????????????????
        if (!shouldSpawn)
        {
            bool mouse = Input.GetMouseButtonDown(0);

            if ((currentTrigger && !_wasTriggerPressed) || mouse)
            {
                shouldSpawn = true;
                spawnSource = mouse ? "mouse" : "trigger";
                if (_cameraTransform != null)
                {
                    finalSpawnPos = _cameraTransform.position + _cameraTransform.forward * 0.5f;
                }
                // ???????? Avatar ???????? (????????????)
                else
                {
                    finalSpawnPos = transform.position + transform.forward * 0.5f;
                }
            }
        }

        // --- 3. ??????? (????????????????) ---
        if (shouldSpawn)
        {
            if (!AquariumColocationGate.IsReady)
            {
                _wasPinching = currentPinch;
                _wasTriggerPressed = currentTrigger;
                return;
            }

            // Fusion may run this callback twice in one tick; share dedupe across all PlayerActions instances.
            if (Runner.Tick == s_spawnConsumedTick && Runner.LocalPlayer == s_spawnConsumedPlayer)
            {
                _wasPinching = currentPinch;
                _wasTriggerPressed = currentTrigger;
                return;
            }

            if (Time.realtimeSinceStartup - _lastLocalSpawnRealtime < 0.35f)
            {
                _wasPinching = currentPinch;
                _wasTriggerPressed = currentTrigger;
                return;
            }

            // ??? finalSpawnPos ???? (0,0,0)?????????????????
            if (finalSpawnPos == Vector3.zero)
            {
                Debug.LogWarning("[PlayerActions] ??????????? 0?????????????????????");
                if (_cameraTransform != null)
                    finalSpawnPos = _cameraTransform.position + _cameraTransform.forward * 0.5f;
                else
                {
                    _wasPinching = currentPinch;
                    _wasTriggerPressed = currentTrigger;
                    return; // ????????????????????????????????
                }
            }

            // Slight forward + down offset so we don't spawn inside the hand or surface.
            bool useTipForward = _localRightHand != null && IsTrustedIndexTip(_localRightHand, _rightIndexTip);
            Vector3 forward = useTipForward ? _rightIndexTip.forward :
                              (_localRightHand != null && _localRightHand.PointerPose != null
                                  ? _localRightHand.PointerPose.forward
                                  : Vector3.forward);
            finalSpawnPos += forward.normalized * 0.02f;

            s_spawnConsumedTick = Runner.Tick;
            s_spawnConsumedPlayer = Runner.LocalPlayer;
            _lastLocalSpawnRealtime = Time.realtimeSinceStartup;
            RPC_SpawnFood(finalSpawnPos);

            float tipDist = -1f;
            Vector3 ptrPos = Vector3.zero;
            if (_localRightHand?.PointerPose != null)
            {
                ptrPos = _localRightHand.PointerPose.position;
                if (_rightIndexTip != null)
                    tipDist = Vector3.Distance(_rightIndexTip.position, ptrPos);
            }

            FishTankLog.Info(
                $"SpawnFood tick={Runner.Tick} hand={_localRightHand?.name} tip={_rightIndexTip?.name} source={spawnSource} pos={finalSpawnPos} ptr={ptrPos} tipDist={tipDist:F3}");

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
        if (MRUK.Instance != null)
        {
            var room = MRUK.Instance.GetCurrentRoom();
            if (room != null && !room.IsPositionInRoom(pos, true))
                FishTankLog.Warn($"Food spawn outside room bounds, kept hand pos={pos}");
        }

        pos = PlaceFoodOnPhysicsSurface(pos);

        var food = Runner.Spawn(FoodPrefab, pos, Quaternion.identity);
        FishTankLog.Info($"Food spawned worldPos={pos} netId={food?.Id}");
    }
}