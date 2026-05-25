using Fusion;
using UnityEngine;
using Meta.XR.MRUtilityKit;

public class FishTankVolume : NetworkBehaviour
{
    public static FishTankVolume Instance { get; private set; }

    [Header("Tank Volume")]
    public Vector3 DefaultHalfExtents = new Vector3(0.4f, 0.3f, 0.3f);
    public float InitialDistanceFromCamera = 0.8f;

    [Header("Manual Placement (optional)")]
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
            if (HalfExtents == default) HalfExtents = DefaultHalfExtents;
            if (Rotation == default) Rotation = Quaternion.identity;

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

        // Keep a reasonable default placement on real furniture so the aquarium is visible,
        // but fish bounds are now handled by MRUK room bounds in SimpleBoid.
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
            if (anchor == null) continue;
            if ((anchor.Label & label) == 0) continue;

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
        if (Vector3.Dot(normal, Vector3.up) < 0f) normal = -normal;

        Center = center + normal.normalized * (HalfExtents.y + 0.05f);
        Rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.forward : Vector3.forward, Vector3.up).normalized,
            Vector3.up);

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

