using Fusion;
using UnityEngine;
using Meta.XR.MRUtilityKit;

public class FishTankVolume : NetworkBehaviour
{
    public static FishTankVolume Instance { get; private set; }

    [Header("Tank Volume")]
    public Vector3 DefaultHalfExtents = new Vector3(0.4f, 0.3f, 0.3f);
    public float InitialDistanceFromCamera = 0.8f;

    [Header("Manual Placement")]
    public float PinchHoldSeconds = 2f;
    public OVRHand.HandFinger PlacementFinger = OVRHand.HandFinger.Middle;
    public Transform GlassBoxVisual;

    [Networked] public Vector3 Center { get; set; }
    [Networked] public Vector3 HalfExtents { get; set; }
    [Networked] public Quaternion Rotation { get; set; }

    private System.Action<MRUKRoom> _roomReadyHandler;

    public static bool TryGetInstance(out FishTankVolume volume)
    {
        volume = Instance;
        return volume != null;
    }

    public override void Spawned()
    {
        Instance = this;

        if (Object.HasStateAuthority)
        {
            HalfExtents = DefaultHalfExtents;
            Rotation = Quaternion.identity;

            var cam = Camera.main;
            if (cam != null && Center == Vector3.zero)
            {
                Center = cam.transform.position + cam.transform.forward * InitialDistanceFromCamera;
                Center = new Vector3(Center.x, Mathf.Max(Center.y, 1.0f), Center.z);
            }

            _roomReadyHandler = OnMRUKRoomReady;
            MRUKBootstrap.SubscribeRoomReady(_roomReadyHandler);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
        if (_roomReadyHandler != null)
        {
            MRUKBootstrap.UnsubscribeRoomReady(_roomReadyHandler);
            _roomReadyHandler = null;
        }
    }

    public override void FixedUpdateNetwork()
    {
        transform.SetPositionAndRotation(Center, Rotation);
        if (GlassBoxVisual != null)
        {
            GlassBoxVisual.localScale = HalfExtents * 2f;
        }
    }

    public Vector3 GetBoundsPull(Vector3 worldPos)
    {
        Quaternion invRot = Quaternion.Inverse(Rotation);
        Vector3 local = invRot * (worldPos - Center);
        Vector3 clamped = new Vector3(
            Mathf.Clamp(local.x, -HalfExtents.x, HalfExtents.x),
            Mathf.Clamp(local.y, -HalfExtents.y, HalfExtents.y),
            Mathf.Clamp(local.z, -HalfExtents.z, HalfExtents.z));

        Vector3 delta = clamped - local;
        if (delta.sqrMagnitude <= 1e-6f) return Vector3.zero;
        return Rotation * delta;
    }

    private void OnMRUKRoomReady(MRUKRoom room)
    {
        if (!Object.HasStateAuthority || room == null) return;

        if (TryPickLargestAnchor(room, MRUKAnchor.SceneLabels.TABLE, out var anchor) ||
            TryPickLargestAnchor(room, MRUKAnchor.SceneLabels.COUCH, out anchor) ||
            TryPickLargestAnchor(room, MRUKAnchor.SceneLabels.FLOOR, out anchor))
        {
            PlaceOnAnchorTop(anchor);
        }
    }

    private bool TryPickLargestAnchor(MRUKRoom room, MRUKAnchor.SceneLabels label, out MRUKAnchor best)
    {
        best = null;
        float bestArea = -1f;

        foreach (var anchor in room.Anchors)
        {
            if (!anchor.Label.HasFlag(label)) continue;

            Vector2 size = anchor.PlaneRect.HasValue ? anchor.PlaneRect.Value.size : Vector2.one;
            float area = Mathf.Abs(size.x * size.y);
            if (area > bestArea)
            {
                bestArea = area;
                best = anchor;
            }
        }

        return best != null;
    }

    private void PlaceOnAnchorTop(MRUKAnchor anchor)
    {
        Vector3 center = anchor.GetAnchorCenter();
        Vector3 normal = anchor.transform.forward;
        if (Vector3.Dot(normal, Vector3.up) < 0f)
        {
            normal = -normal;
        }

        Center = center + normal.normalized * (HalfExtents.y + 0.05f);
        Rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.forward : Vector3.forward, Vector3.up).normalized, Vector3.up);

        if (anchor.PlaneRect.HasValue)
        {
            Vector2 size = anchor.PlaneRect.Value.size;
            HalfExtents = new Vector3(
                Mathf.Min(DefaultHalfExtents.x, Mathf.Max(0.2f, Mathf.Abs(size.x) * 0.45f)),
                DefaultHalfExtents.y,
                Mathf.Min(DefaultHalfExtents.z, Mathf.Max(0.2f, Mathf.Abs(size.y) * 0.45f)));
        }

        Debug.Log($"[FishTankVolume] Placed tank on MRUK anchor: {anchor.Label}");
    }
}
using UnityEngine;
using Fusion;
using Meta.XR.MRUtilityKit;

/// <summary>
/// 网络同步的"虚拟鱼缸"体积。
///
/// - 权威端（Host）用右手 **中指** 长按 pinch 2 秒，把鱼缸放置在真实表面上：
///   调用 MRUK 的 TryGetClosestSurfacePosition 取到最近的墙/桌/地法线，鱼缸底部贴在其上。
/// - Center / HalfExtents 通过 Fusion [Networked] 同步给所有 Client。
/// - SimpleBoid 通过 FishTankVolume.Instance 读取 AABB 限制鱼群活动范围。
///
/// 注：右手食指 pinch 仍然用于投食（见 PlayerActions.cs），互不冲突。
/// </summary>
public class FishTankVolume : NetworkBehaviour
{
    public static FishTankVolume Instance { get; private set; }

    [Header("Networked State (Don't edit at runtime)")]
    [Networked] public Vector3 Center { get; set; }
    [Networked] public Vector3 HalfExtents { get; set; }
    [Networked] public NetworkBool IsPlacedOnSurface { get; set; }

    [Header("Defaults")]
    [Tooltip("默认鱼缸半尺寸（米）。")]
    public Vector3 DefaultHalfExtents = new Vector3(0.4f, 0.3f, 0.3f);

    [Tooltip("未放置时的初始中心（相对相机前方距离）。")]
    public float InitialDistanceFromCamera = 0.8f;

    [Header("Placement Gesture (Host Only)")]
    [Tooltip("长按中指 pinch 多久触发放置。")]
    public float PinchHoldSeconds = 2.0f;

    [Tooltip("使用哪根手指来触发放置（默认中指，避免跟食指投食冲突）。")]
    public OVRHand.HandFinger PlacementFinger = OVRHand.HandFinger.Middle;

    [Header("Visualization")]
    [Tooltip("可选：一个子 GameObject，运行时会被按 Center/HalfExtents 摆放。建议是半透明玻璃材质的 Cube。")]
    public Transform GlassBoxVisual;

    private OVRHand _localRightHand;
    private float _pinchTimer = 0f;
    private float _findHandCooldown = 0f;
    private System.Action<MRUKRoom> _roomReadyHandler;

    public override void Spawned()
    {
        Instance = this;

        if (Object.HasStateAuthority)
        {
            // 初始默认值
            if (HalfExtents == Vector3.zero) HalfExtents = DefaultHalfExtents;

            if (!IsPlacedOnSurface)
            {
                if (Camera.main != null)
                {
                    Center = Camera.main.transform.position + Camera.main.transform.forward * InitialDistanceFromCamera;
                }
                else
                {
                    Center = new Vector3(0, 1.5f, 1f);
                }
            }

            // Host subscribes to MRUK room-ready so the tank auto-docks to a
            // real surface as soon as the room is scanned. Avoids requiring
            // the user to do a long-press pinch before seeing any fish activity
            // in the correct place.
            _roomReadyHandler = OnMRUKRoomReady;
            MRUKBootstrap.SubscribeRoomReady(_roomReadyHandler);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
        if (_roomReadyHandler != null)
        {
            MRUKBootstrap.UnsubscribeRoomReady(_roomReadyHandler);
            _roomReadyHandler = null;
        }
    }

    /// <summary>
    /// Called (Host only) when MRUK finishes loading the room. Chooses a
    /// sensible default anchor to dock the fish tank on top of, with
    /// progressive fallbacks so we never end up pinned to the user's head.
    /// </summary>
    void OnMRUKRoomReady(MRUKRoom room)
    {
        if (!Object.HasStateAuthority || room == null) return;

        // If user already placed the tank manually this session, respect it.
        if (IsPlacedOnSurface) return;

        // Priority 1: largest TABLE / DESK (stuff you'd put a tank on).
        if (TryPickLargestAnchor(room,
                MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH,
                out var anchor))
        {
            PlaceOnAnchorTop(anchor);
            return;
        }

        // Priority 2: floor center, lifted up to a comfortable viewing height.
        if (room.FloorAnchors != null && room.FloorAnchors.Count > 0)
        {
            var floor = room.FloorAnchors[0];
            Vector3 floorPos = floor.transform.position;
            Vector3 up = floor.transform.up;
            // Hover at ~0.8m above the floor so the tank sits at roughly a
            // standing user's waist height instead of embedded in the floor.
            Center = floorPos + up * (0.8f + HalfExtents.y);
            IsPlacedOnSurface = true;
            Debug.Log("[FishTankVolume] Auto-placed above floor center (no table/desk/couch anchors).");
            return;
        }

        // Last resort: leave the "head-forward" fallback set in Spawned().
        Debug.LogWarning("[FishTankVolume] Room loaded but no usable anchors found; keeping head-forward fallback.");
    }

    bool TryPickLargestAnchor(MRUKRoom room, MRUKAnchor.SceneLabels desiredLabels, out MRUKAnchor best)
    {
        best = null;
        float bestArea = -1f;
        if (room.Anchors == null) return false;
        foreach (var a in room.Anchors)
        {
            if (a == null) continue;
            if (!a.HasAnyLabel(desiredLabels)) continue;
            float area = 0f;
            if (a.PlaneRect.HasValue)
            {
                var sz = a.PlaneRect.Value.size;
                area = Mathf.Abs(sz.x * sz.y);
            }
            if (area > bestArea)
            {
                bestArea = area;
                best = a;
            }
        }
        return best != null;
    }

    void PlaceOnAnchorTop(MRUKAnchor anchor)
    {
        // MRUK convention for plane anchors: transform.forward is the plane's
        // outward-facing normal. For a TABLE/COUCH the top plane faces up, so
        // transform.forward ~= world up. For a FLOOR the forward also points
        // up. transform.position sits at the plane's center.
        Vector3 surfacePos = anchor.GetAnchorCenter();
        Vector3 normal = anchor.transform.forward;
        if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;

        // Shrink the tank's horizontal extents to fit the real surface so
        // fish don't try to swim beyond the real table / couch edges.
        if (anchor.PlaneRect.HasValue)
        {
            var size = anchor.PlaneRect.Value.size;
            float maxHalfX = Mathf.Max(0.1f, size.x * 0.5f - 0.05f);
            float maxHalfZ = Mathf.Max(0.1f, size.y * 0.5f - 0.05f);
            HalfExtents = new Vector3(
                Mathf.Min(DefaultHalfExtents.x, maxHalfX),
                DefaultHalfExtents.y,
                Mathf.Min(DefaultHalfExtents.z, maxHalfZ));
        }

        Center = surfacePos + normal * HalfExtents.y;
        IsPlacedOnSurface = true;
        Debug.Log($"[FishTankVolume] Auto-placed on '{anchor.name}' (label={anchor.Label})");
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        if (_localRightHand == null)
        {
            _findHandCooldown -= Runner.DeltaTime;
            if (_findHandCooldown <= 0f)
            {
                TryFindRightHand();
                _findHandCooldown = 1.0f;
            }
        }

        if (_localRightHand == null || !_localRightHand.IsTracked || _localRightHand.PointerPose == null)
        {
            _pinchTimer = 0f;
            return;
        }

        bool isPinching = _localRightHand.GetFingerIsPinching(PlacementFinger);
        if (isPinching)
        {
            _pinchTimer += Runner.DeltaTime;
            if (_pinchTimer >= PinchHoldSeconds)
            {
                PlaceOnRealSurface(_localRightHand.PointerPose.position);
                _pinchTimer = 0f;
            }
        }
        else
        {
            _pinchTimer = 0f;
        }
    }

    void TryFindRightHand()
    {
        var hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
        foreach (var h in hands)
        {
            if (h.PointerPose != null && (h.name.Contains("Right") || h.PointerPose.name.Contains("Right")))
            {
                _localRightHand = h;
                return;
            }
        }
    }

    void PlaceOnRealSurface(Vector3 handPos)
    {
        if (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
        {
            Center = handPos;
            IsPlacedOnSurface = false;
            Debug.LogWarning("[FishTankVolume] MRUK not ready; placed at raw hand position.");
            return;
        }

        var room = MRUK.Instance.GetCurrentRoom();
        float dist = room.TryGetClosestSurfacePosition(handPos, out Vector3 surfPos, out MRUKAnchor anchor, out Vector3 normal);

        if (dist >= 0f)
        {
            Center = surfPos + normal * HalfExtents.y;
            IsPlacedOnSurface = true;
            Debug.Log($"[FishTankVolume] Placed on surface (anchor={anchor?.name}, dist={dist:F2}m)");
        }
        else
        {
            Center = handPos;
            IsPlacedOnSurface = false;
        }
    }

    public override void Render()
    {
        if (GlassBoxVisual != null)
        {
            GlassBoxVisual.position = Center;
            GlassBoxVisual.localScale = HalfExtents * 2f;
        }
    }

    public static bool TryGetInstance(out FishTankVolume v)
    {
        v = Instance;
        return v != null;
    }

    /// <summary>
    /// 若 worldPos 在缸外，返回一个指向缸内的推力向量；缸内返回 Vector3.zero。
    /// </summary>
    public Vector3 GetBoundsPull(Vector3 worldPos)
    {
        Vector3 local = worldPos - Center;
        Vector3 over = new Vector3(
            Mathf.Max(0f, Mathf.Abs(local.x) - HalfExtents.x) * Mathf.Sign(local.x),
            Mathf.Max(0f, Mathf.Abs(local.y) - HalfExtents.y) * Mathf.Sign(local.y),
            Mathf.Max(0f, Mathf.Abs(local.z) - HalfExtents.z) * Mathf.Sign(local.z));
        return -over;
    }

    public bool Contains(Vector3 worldPos)
    {
        Vector3 d = worldPos - Center;
        return Mathf.Abs(d.x) <= HalfExtents.x
            && Mathf.Abs(d.y) <= HalfExtents.y
            && Mathf.Abs(d.z) <= HalfExtents.z;
    }
}
