using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Meta.XR.MRUtilityKit;
using UnityEngine;

/// <summary>
/// Host: OVRColocationSession advertise + MRUK ShareRoomAsync.
/// Client: discovery + LoadSceneFromSharedRooms with host floor alignment.
/// Editor / non-Android: skips to ready for local testing.
/// </summary>
public class AquariumColocationController : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] float startupTimeoutSeconds = 90f;
    [SerializeField] float mrukWaitSeconds = 45f;

    [Header("Solo Host")]
    [Tooltip("Only the Host is in session: allow spawn after MRUK even if Colocation advertise/share fails.")]
    [SerializeField] bool allowSoloHostBypass = true;

    [Header("References (optional — resolved at runtime)")]
    [SerializeField] Transform trackingSpace;
    [SerializeField] Transform centerEyeCamera;

    bool _sessionStarted;
    bool _discoveryRegistered;
    Guid _groupId;
    Coroutine _startupRoutine;

    void OnEnable()
    {
        AquariumColocationGate.Reset();
        _startupRoutine = StartCoroutine(WaitForRunnerThenBegin());
    }

    void OnDisable()
    {
        if (_startupRoutine != null)
            StopCoroutine(_startupRoutine);
        TeardownColocationSession();
    }

    IEnumerator WaitForRunnerThenBegin()
    {
        float deadline = Time.realtimeSinceStartup + startupTimeoutSeconds;
        NetworkRunner runner = null;

        while (Time.realtimeSinceStartup < deadline)
        {
            runner = FindAnyObjectByType<NetworkRunner>();
            if (runner != null && runner.IsRunning)
                break;
            yield return null;
            runner = null;
        }

        if (runner == null || !runner.IsRunning)
        {
            FishTankLog.Warn("Colocation: no running NetworkRunner — staying not ready.");
            AquariumColocationGate.SetStatus("未连接网络会话");
            yield break;
        }

        ResolveRigReferences();
        FishTankLog.Info($"Colocation: runner active, IsServer={runner.IsServer}");
        BeginForRunner(runner);
    }

    void ResolveRigReferences()
    {
        if (centerEyeCamera == null && Camera.main != null)
            centerEyeCamera = Camera.main.transform;

        if (trackingSpace != null) return;

        if (centerEyeCamera != null)
        {
            var ovrRig = centerEyeCamera.GetComponentInParent<OVRCameraRig>();
            if (ovrRig != null)
                trackingSpace = ovrRig.transform;
        }

        if (trackingSpace == null && centerEyeCamera != null)
            trackingSpace = centerEyeCamera.root;
    }

    void BeginForRunner(NetworkRunner runner)
    {
        if (_sessionStarted) return;
        _sessionStarted = true;

#if UNITY_EDITOR
        if (!Application.isMobilePlatform)
        {
            FishTankLog.Info("Colocation: editor/desktop bypass — marking ready.");
            AquariumColocationGate.SetReady("编辑器跳过 Colocation");
            return;
        }
#endif

        if (runner.IsServer)
            StartCoroutine(HostColocationRoutine());
        else
            StartCoroutine(ClientColocationRoutine());
    }

    IEnumerator HostColocationRoutine()
    {
        var runner = FindAnyObjectByType<NetworkRunner>();
        FishTankLog.Info("Colocation Host routine started");

        AquariumColocationGate.SetStatus("Host：等待 MRUK 房间…");
        yield return WaitForMrukRoomCoroutine();

        var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : MRUKBootstrap.LastLoadedRoom;
        if (room == null)
        {
            FishTankLog.Warn("Colocation Host: MRUK room missing after wait.");
            if (allowSoloHostBypass && IsSoloHost(runner))
                AquariumColocationGate.SetReady("单人 Host：MRUK 未就绪，降级放行");
            else
                AquariumColocationGate.SetStatus("MRUK 房间未就绪");
            yield break;
        }

        FishTankLog.Info($"Colocation Host: MRUK room ready, anchors={room.Anchors?.Count ?? 0}");

        bool advertised = false;
        bool shared = false;

        AquariumColocationGate.SetStatus("Host：广播 Colocation 会话…");
        var advertiseTask = StartAdvertisementWithRoomPayload(room);
        while (!advertiseTask.IsCompleted)
            yield return null;
        advertised = advertiseTask.Result;

        if (advertised)
        {
            AquariumColocationGate.SetStatus("Host：共享 MRUK 房间…");
            var shareTask = ShareMrukRoomAsync(room, _groupId);
            while (!shareTask.IsCompleted)
                yield return null;
            shared = shareTask.Result;
        }

        if (shared)
        {
            AquariumColocationGate.SetReady("Host 空间已共享");
            yield break;
        }

        if (allowSoloHostBypass && IsSoloHost(runner))
        {
            FishTankLog.Warn(
                $"Colocation Host solo bypass (advertised={advertised}, shared={shared}). Spawn will proceed.");
            AquariumColocationGate.SetReady("单人 Host：跳过 Colocation 共享");
            yield break;
        }

        AquariumColocationGate.SetStatus(advertised ? "MRUK 房间共享失败" : "Colocation 广播失败");
        FishTankLog.Warn($"Colocation Host blocked spawn (advertised={advertised}, shared={shared}).");
    }

    static bool IsSoloHost(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning) return true;
        int count = 0;
        foreach (var _ in runner.ActivePlayers)
            count++;
        return count <= 1;
    }

    IEnumerator ClientColocationRoutine()
    {
        AquariumColocationGate.SetStatus("Client：搜索附近 Colocation…");
        RegisterDiscovery();

        float deadline = Time.realtimeSinceStartup + startupTimeoutSeconds;
        while (!AquariumColocationGate.IsReady && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!AquariumColocationGate.IsReady)
        {
            FishTankLog.Warn("Colocation Client: timed out waiting for host advertisement.");
            AquariumColocationGate.SetStatus("未找到 Host 空间会话");
        }
    }

    IEnumerator WaitForMrukRoomCoroutine()
    {
        float deadline = Time.realtimeSinceStartup + mrukWaitSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null)
                room = MRUKBootstrap.LastLoadedRoom;
            if (room != null)
                yield break;
            yield return null;
        }
    }

    async Task<bool> StartAdvertisementWithRoomPayload(MRUKRoom room)
    {
        try
        {
            if (!TryGetFloorTransform(room, out Transform floor))
            {
                FishTankLog.Warn("Colocation Host: no floor anchor on MRUK room.");
                return false;
            }

            if (!TryGetRoomAnchorUuid(room, out var roomUuid))
            {
                FishTankLog.Warn("Colocation Host: room anchor UUID unavailable.");
                return false;
            }

            var payload = new ColocationAdvertisementPayload
            {
                session = AquariumSessionConfig.FusionSessionName,
                roomUuid = roomUuid.ToString(),
                floorPose = PoseUtility.FormatPose(new Pose(floor.position, floor.rotation))
            };

            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            var result = await OVRColocationSession.StartAdvertisementAsync(bytes);

            if (!result.Success || !result.TryGetValue(out var sessionUuid))
            {
                FishTankLog.Warn($"Colocation advertisement failed: {result.Status}");
                return false;
            }

            _groupId = sessionUuid;
            FishTankLog.Info($"Colocation Host advertising group={_groupId}");
            return true;
        }
        catch (Exception ex)
        {
            FishTankLog.Warn($"Colocation advertisement exception: {ex.Message}");
            return false;
        }
    }

    async Task<bool> ShareMrukRoomAsync(MRUKRoom room, Guid groupUuid)
    {
        try
        {
            var result = await room.ShareRoomAsync(groupUuid);
            if (!result.Success)
            {
                FishTankLog.Warn($"MRUK ShareRoomAsync failed: {result.Status}");
                return false;
            }

            FishTankLog.Info($"MRUK room shared to group {groupUuid}");
            return true;
        }
        catch (Exception ex)
        {
            FishTankLog.Warn($"MRUK ShareRoomAsync exception: {ex.Message}");
            return false;
        }
    }

    void RegisterDiscovery()
    {
        if (_discoveryRegistered) return;
        OVRColocationSession.ColocationSessionDiscovered += OnColocationSessionDiscovered;
        _discoveryRegistered = true;

        StartCoroutine(StartDiscoveryCoroutine());
    }

    IEnumerator StartDiscoveryCoroutine()
    {
        var task = OVRColocationSession.StartDiscoveryAsync();
        while (!task.IsCompleted)
            yield return null;

        if (!task.TryGetResult(out var result))
        {
            FishTankLog.Warn("StartDiscoveryAsync completed without a retrievable result.");
            yield break;
        }

        if (!result.Success)
            FishTankLog.Warn($"StartDiscoveryAsync failed: {result.Status}");
    }

    void OnColocationSessionDiscovered(OVRColocationSession.Data data)
    {
        if (AquariumColocationGate.IsReady) return;

        if (!TryParsePayload(data, out var payload, out var groupId))
            return;

        if (!string.Equals(payload.session, AquariumSessionConfig.FusionSessionName, StringComparison.Ordinal))
            return;

        _groupId = groupId;
        FishTankLog.Info($"Colocation Client discovered host group={groupId}");
        AquariumColocationGate.SetStatus("Client：加载共享房间…");
        _ = LoadSharedRoomForClientAsync(payload, groupId);
    }

    static bool TryParsePayload(OVRColocationSession.Data data, out ColocationAdvertisementPayload payload, out Guid groupId)
    {
        payload = null;
        groupId = Guid.Empty;

        try
        {
            groupId = data.AdvertisementUuid;
            byte[] meta = data.Metadata;
            if (meta == null || meta.Length == 0)
                return false;

            string json = Encoding.UTF8.GetString(meta);
            payload = JsonUtility.FromJson<ColocationAdvertisementPayload>(json);
            return payload != null && !string.IsNullOrEmpty(payload.session);
        }
        catch (Exception ex)
        {
            FishTankLog.Warn($"Colocation payload parse failed: {ex.Message}");
            return false;
        }
    }

    async Task LoadSharedRoomForClientAsync(ColocationAdvertisementPayload payload, Guid groupId)
    {
        try
        {
            if (MRUK.Instance == null)
            {
                FishTankLog.Warn("Colocation Client: MRUK.Instance null.");
                return;
            }

            if (!Guid.TryParse(payload.roomUuid, out var roomUuid))
            {
                FishTankLog.Warn($"Colocation Client: invalid room uuid '{payload.roomUuid}'.");
                return;
            }

            if (!PoseUtility.TryParsePose(payload.floorPose, out var floorPoseOnHost))
            {
                FishTankLog.Warn("Colocation Client: invalid floor pose in advertisement.");
                return;
            }

            var roomIds = new List<Guid> { roomUuid };
            var alignment = (roomUuid, floorPoseOnHost);

            var loadResult = await MRUK.Instance.LoadSceneFromSharedRooms(
                roomIds,
                groupId,
                alignment,
                removeMissingRooms: true);

            FishTankLog.Info($"Colocation Client LoadSceneFromSharedRooms result={loadResult}");

            AlignTrackingSpaceToFloor(floorPoseOnHost);
            AquariumColocationGate.SetReady("Client 已对齐 Host 房间");
        }
        catch (Exception ex)
        {
            FishTankLog.Warn($"Colocation Client load failed: {ex.Message}");
            AquariumColocationGate.SetStatus("加载共享房间失败");
        }
    }

    void AlignTrackingSpaceToFloor(Pose hostFloorPose)
    {
        if (trackingSpace == null || centerEyeCamera == null)
        {
            ResolveRigReferences();
            if (trackingSpace == null) return;
        }

        var localRoom = MRUK.Instance?.GetCurrentRoom();
        if (!TryGetFloorTransform(localRoom, out Transform localFloor))
        {
            FishTankLog.Warn("Colocation: no local floor anchor after shared room load.");
            return;
        }

        Pose localPose = new Pose(localFloor.position, localFloor.rotation);
        Vector3 posDelta = hostFloorPose.position - localPose.position;
        trackingSpace.position += posDelta;

        float yawDelta = hostFloorPose.rotation.eulerAngles.y - localPose.rotation.eulerAngles.y;
        trackingSpace.RotateAround(centerEyeCamera.position, Vector3.up, yawDelta);

        FishTankLog.Info($"Colocation aligned rig deltaPos={posDelta} yaw={yawDelta:F1}");
    }

    static bool TryGetFloorTransform(MRUKRoom room, out Transform floor)
    {
        floor = null;
        if (room == null) return false;

        if (room.FloorAnchor != null)
        {
            floor = room.FloorAnchor.transform;
            return floor != null;
        }

        if (room.FloorAnchors != null && room.FloorAnchors.Count > 0 && room.FloorAnchors[0] != null)
        {
            floor = room.FloorAnchors[0].transform;
            return floor != null;
        }

        foreach (var anchor in room.Anchors)
        {
            if (anchor == null) continue;
            if ((anchor.Label & MRUKAnchor.SceneLabels.FLOOR) == 0) continue;
            floor = anchor.transform;
            return floor != null;
        }

        return false;
    }

    static bool TryGetRoomAnchorUuid(MRUKRoom room, out Guid uuid)
    {
        uuid = Guid.Empty;
        if (room == null) return false;
        try
        {
            var anchor = room.Anchor;
            uuid = anchor.Uuid;
            return uuid != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }

    void TeardownColocationSession()
    {
        if (_discoveryRegistered)
        {
            OVRColocationSession.ColocationSessionDiscovered -= OnColocationSessionDiscovered;
            _discoveryRegistered = false;
        }

        try
        {
            _ = OVRColocationSession.StopDiscoveryAsync();
            _ = OVRColocationSession.StopAdvertisementAsync();
        }
        catch (Exception ex)
        {
            FishTankLog.Warn($"Colocation teardown: {ex.Message}");
        }
    }

    [Serializable]
    class ColocationAdvertisementPayload
    {
        public string session;
        public string roomUuid;
        public string floorPose;
    }

    static class PoseUtility
    {
        public static string FormatPose(Pose pose) =>
            $"{pose.position.x},{pose.position.y},{pose.position.z}," +
            $"{pose.rotation.x},{pose.rotation.y},{pose.rotation.z},{pose.rotation.w}";

        public static bool TryParsePose(string data, out Pose pose)
        {
            pose = default;
            if (string.IsNullOrEmpty(data)) return false;
            var p = data.Split(',');
            if (p.Length < 7) return false;
            if (!float.TryParse(p[0], out float px) || !float.TryParse(p[1], out float py) ||
                !float.TryParse(p[2], out float pz) || !float.TryParse(p[3], out float qx) ||
                !float.TryParse(p[4], out float qy) || !float.TryParse(p[5], out float qz) ||
                !float.TryParse(p[6], out float qw))
                return false;
            pose = new Pose(new Vector3(px, py, pz), new Quaternion(qx, qy, qz, qw));
            return true;
        }
    }
}
