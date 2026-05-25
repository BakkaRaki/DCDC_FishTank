using System.Collections;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Meta.XR.MRUtilityKit;

public class MRUKBootstrap : MonoBehaviour
{
    public static event System.Action<MRUKRoom> RoomReady;
    public static MRUKRoom LastLoadedRoom { get; private set; }
    public static MRUKBootstrap Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<MRUKBootstrap>() != null) return;
        var go = new GameObject("[Auto] MRUKBootstrap");
        DontDestroyOnLoad(go);
        go.AddComponent<MRUKBootstrap>();
    }

    public static void SubscribeRoomReady(System.Action<MRUKRoom> handler)
    {
        if (handler == null) return;
        RoomReady += handler;
        if (LastLoadedRoom != null)
        {
            try { handler.Invoke(LastLoadedRoom); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }

    public static void UnsubscribeRoomReady(System.Action<MRUKRoom> handler)
    {
        if (handler == null) return;
        RoomReady -= handler;
    }

    [Header("Scene Load")]
    public bool LoadSceneOnDevice = true;
    public TextAsset EditorSceneJson;

    [Header("EffectMesh Colliders")]
    public bool CreateEffectMeshColliders = true;
    public string RealWorldLayerName = "RealWorld";
    public MRUKAnchor.SceneLabels Labels =
        MRUKAnchor.SceneLabels.GLOBAL_MESH |
        MRUKAnchor.SceneLabels.FLOOR |
        MRUKAnchor.SceneLabels.CEILING |
        MRUKAnchor.SceneLabels.WALL_FACE |
        MRUKAnchor.SceneLabels.INNER_WALL_FACE |
        MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE |
        MRUKAnchor.SceneLabels.TABLE |
        MRUKAnchor.SceneLabels.COUCH |
        MRUKAnchor.SceneLabels.STORAGE |
        MRUKAnchor.SceneLabels.WINDOW_FRAME |
        MRUKAnchor.SceneLabels.OTHER;

    [Header("Rescan")]
    [Tooltip("Wait for OpenXR session before Space Setup / scene load (avoids xrGetSpaceRoomLayoutFB invalid handle).")]
    public float XrReadyTimeoutSeconds = 20f;
    public bool PromptForRoomScanOnStartup = true;
    public bool SuppressBoundaryVisibility = false;
    [Tooltip("If loaded room has fewer anchors than this, trigger Space Setup automatically.")]
    public int MinAnchorsForValidRoom = 1;

    [Header("Anchor Physics Fallback")]
    [Tooltip("Add BoxCollider proxies on MRUK plane anchors (thickens walls; supplements thin EffectMesh).")]
    public bool CreateAnchorPhysicsFallback = true;
    [Tooltip("Also add anchor proxies when EffectMesh colliders exist (recommended for food physics).")]
    public bool SupplementEffectMeshPhysics = true;
    public float AnchorPhysicsWallThickness = 0.12f;
    public float AnchorPhysicsFloorThickness = 0.08f;

    private MRUK _mruk;
    private EffectMesh _effectMesh;
    private Transform _anchorPhysicsRoot;
    private Coroutine _effectMeshAuditRoutine;
    private Coroutine _bootstrapRoutine;
    private bool _rescanInFlight;
    private static bool _startupScanRequestedThisSession;

    void Awake()
    {
        Instance = this;

        _mruk = FindAnyObjectByType<MRUK>();
        if (_mruk == null)
        {
            var mrukGO = new GameObject("MRUK");
            DontDestroyOnLoad(mrukGO);
            _mruk = mrukGO.AddComponent<MRUK>();
        }
        else
        {
            DontDestroyOnLoad(_mruk.gameObject);
        }

        var settings = new MRUK.MRUKSettings
        {
            DataSource = MRUK.SceneDataSource.Device,
            // Never auto-load cached scene in Awake — wait for XR + Space Setup first.
            LoadSceneOnStartup = false
        };

#if UNITY_EDITOR
        if (EditorSceneJson != null)
        {
            settings.DataSource = MRUK.SceneDataSource.Json;
            settings.SceneJsons = new TextAsset[] { EditorSceneJson };
            settings.LoadSceneOnStartup = true;
        }
#endif
        _mruk.SceneSettings = settings;

        if (CreateEffectMeshColliders)
        {
            var emGO = new GameObject("EffectMesh_Collider");
            emGO.transform.SetParent(_mruk.transform, false);
            _effectMesh = emGO.AddComponent<EffectMesh>();
            _effectMesh.Labels = Labels;
            _effectMesh.HideMesh = true;
            _effectMesh.Colliders = true;
            _effectMesh.CastShadow = false;
            EnsureEffectMeshMaterial();

            int layer = LayerMask.NameToLayer(RealWorldLayerName);
            if (layer >= 0) _effectMesh.Layer = layer;
        }

        _mruk.RoomCreatedEvent.AddListener(OnRoomCreated);
        _mruk.RoomUpdatedEvent.AddListener(OnRoomUpdated);

        if (GetComponent<MRUKRescanInput>() == null)
        {
            gameObject.AddComponent<MRUKRescanInput>();
        }

        MRFoodPhysics.EnsureFoodCollidesWithRealWorld();
        ApplyBoundaryVisibilityPolicy();
    }

    void Start()
    {
#if !UNITY_EDITOR
        if (LoadSceneOnDevice && _bootstrapRoutine == null)
            _bootstrapRoutine = StartCoroutine(CoBootstrapSceneAfterXrReady());
#endif
    }

    IEnumerator CoBootstrapSceneAfterXrReady()
    {
        float deadline = Time.realtimeSinceStartup + XrReadyTimeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (OVRManager.instance != null)
                break;
            yield return null;
        }

        if (OVRManager.instance == null)
        {
            Debug.LogWarning("[MRUKBootstrap] OVRManager not found; loading scene without XR-ready gate.");
        }
        else
        {
            // Wait for HMD + a short settle window so OpenXR session is ready before room-layout queries.
            const int maxWaitFrames = 120;
            const int settleFramesAfterHmd = 30;
            int waited = 0;
            while (waited < maxWaitFrames && !OVRManager.isHmdPresent)
            {
                waited++;
                yield return null;
            }

            for (int i = 0; i < settleFramesAfterHmd; i++)
                yield return null;
        }

        if (_startupScanRequestedThisSession)
            yield break;

        _startupScanRequestedThisSession = true;

        if (PromptForRoomScanOnStartup)
        {
            Debug.Log("[MRUKBootstrap] XR ready — running Space Setup then LoadSceneFromDevice.");
            var task = RequestRescan();
            while (!task.IsCompleted)
                yield return null;
        }
        else
        {
            var loadTask = LoadSceneFromDeviceAsync();
            while (!loadTask.IsCompleted)
                yield return null;
            yield return CoRefreshEffectMeshAfterSceneReload();
        }

        _bootstrapRoutine = null;
    }

    async Task LoadSceneFromDeviceAsync()
    {
        if (MRUK.Instance == null) return;
        await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true, removeMissingRooms: true);
    }

    public async Task RequestRescan()
    {
        if (_rescanInFlight) return;

        _rescanInFlight = true;
        try
        {
#if UNITY_EDITOR
            await Task.Yield();
            Debug.Log("[MRUKBootstrap] RequestRescan is a no-op in the Editor.");
#else
            bool captured = await OVRScene.RequestSpaceSetup();
            Debug.Log($"[MRUKBootstrap] Space Setup returned: {captured}");

            if (MRUK.Instance != null)
            {
                await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true, removeMissingRooms: true);
            }

            StartCoroutine(CoRefreshEffectMeshAfterSceneReload());
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            _rescanInFlight = false;
        }
    }

    void OnRoomCreated(MRUKRoom room) => HandleRoomReady(room);
    void OnRoomUpdated(MRUKRoom room) => HandleRoomReady(room);

    void HandleRoomReady(MRUKRoom room)
    {
        LogRoomDiagnostics(room);

        if (room != null && CountAnchors(room) < MinAnchorsForValidRoom && !_rescanInFlight)
        {
            Debug.LogWarning($"[MRUKBootstrap] Room has < {MinAnchorsForValidRoom} anchors (likely missing room layout / Space Setup). Scheduling rescan.");
            _ = RequestRescan();
            return;
        }

        if (_effectMesh != null && CreateEffectMeshColliders)
        {
            TryCreateEffectMeshForRoom(room);
            if (_effectMeshAuditRoutine != null)
                StopCoroutine(_effectMeshAuditRoutine);
            _effectMeshAuditRoutine = StartCoroutine(VerifyEffectMeshCollidersWhenReady(room));
        }

        if (CreateAnchorPhysicsFallback && (SupplementEffectMeshPhysics || _effectMesh == null))
            EnsureAnchorPhysicsColliders(room);

        MRFoodPhysics.UpdateRoomCache(room);

        ApplyBoundaryVisibilityPolicy();
        LastLoadedRoom = room;

        var handler = RoomReady;
        if (handler != null)
        {
            try { handler.Invoke(room); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }

    static int CountAnchors(MRUKRoom room)
    {
        if (room?.Anchors == null) return 0;
        int n = 0;
        foreach (var a in room.Anchors)
        {
            if (a != null) n++;
        }
        return n;
    }

    static void LogRoomDiagnostics(MRUKRoom room)
    {
        if (room == null)
        {
            Debug.LogWarning("[MRUKBootstrap] Room diagnostics: room is null.");
            return;
        }

        int total = 0;
        int withPlane = 0;
        var labelCounts = new Dictionary<MRUKAnchor.SceneLabels, int>();

        foreach (var anchor in room.Anchors)
        {
            if (anchor == null) continue;
            total++;
            if (anchor.PlaneRect.HasValue) withPlane++;

            MRUKAnchor.SceneLabels label = anchor.Label;
            if (!labelCounts.ContainsKey(label))
                labelCounts[label] = 0;
            labelCounts[label]++;
        }

        bool inRoom = false;
        if (Camera.main != null)
            inRoom = room.IsPositionInRoom(Camera.main.transform.position, true);

        var labelSummary = new System.Text.StringBuilder();
        foreach (var kv in labelCounts)
            labelSummary.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');

        FishTankLog.Info($"Room anchors={total} withPlane={withPlane} cameraInRoom={inRoom} labels=[{labelSummary}]");
    }

    /// <summary>Recreate anchor proxies and refresh food floor cache (e.g. after Fusion scene load).</summary>
    public void RefreshFoodSupportPhysics()
    {
        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        room ??= LastLoadedRoom;
        if (room == null) return;

        if (CreateAnchorPhysicsFallback && (SupplementEffectMeshPhysics || _effectMesh == null))
            EnsureAnchorPhysicsColliders(room);

        MRFoodPhysics.PrepareForFoodSpawn();
    }

    void EnsureAnchorPhysicsColliders(MRUKRoom room)
    {
        if (!CreateAnchorPhysicsFallback || room?.Anchors == null) return;

        if (_anchorPhysicsRoot != null)
            Destroy(_anchorPhysicsRoot.gameObject);

        var rootGo = new GameObject("MRUK_AnchorPhysics");
        rootGo.transform.SetParent(transform, false);
        _anchorPhysicsRoot = rootGo.transform;

        int layer = LayerMask.NameToLayer(RealWorldLayerName);
        if (layer < 0) layer = 0;

        int created = 0;
        int standableCount = 0;
        int tableCount = 0;
        foreach (var anchor in room.Anchors)
        {
            if (anchor == null || !anchor.PlaneRect.HasValue) continue;

            string labelName = GetProxyLabelName(anchor.Label);
            var proxy = new GameObject($"PhysicsProxy_{labelName}");
            proxy.layer = layer;

            Vector2 size = anchor.PlaneRect.Value.size;
            bool isWall = (anchor.Label & (MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE |
                                           MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE)) != 0;
            bool isFloor = (anchor.Label & MRUKAnchor.SceneLabels.FLOOR) != 0;
            bool isFurniture = (anchor.Label & (MRUKAnchor.SceneLabels.TABLE |
                                                MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.STORAGE)) != 0;
            // FLOOR uses EffectMesh colliders; proxy at anchor center is often wrong Y and breaks food landing.
            if (isFloor)
                continue;
            float thickness = isWall ? AnchorPhysicsWallThickness : AnchorPhysicsFloorThickness;
            thickness = Mathf.Max(thickness, 0.08f);

            float extentX = Mathf.Max(0.2f, Mathf.Abs(size.x));
            float extentY = Mathf.Max(0.2f, Mathf.Abs(size.y));

            Vector3 standNormal = MRFoodPhysics.GetStandableNormal(anchor.transform);
            bool standable = isFurniture && !isWall;

            var box = proxy.AddComponent<BoxCollider>();
            box.isTrigger = false;

            if (standable)
            {
                // Horizontal top surface in world space (anchor.forward is often wrong for TABLE).
                Vector3 up = standNormal.y >= 0.5f ? standNormal : Vector3.up;
                if ((anchor.Label & MRUKAnchor.SceneLabels.TABLE) != 0)
                {
                    extentX *= 1.3f;
                    extentY *= 1.3f;
                }

                proxy.transform.SetParent(_anchorPhysicsRoot, true);
                Vector3 surfaceTop = MRFoodPhysics.GetAnchorStandableSurfacePoint(anchor, up);
                proxy.transform.SetPositionAndRotation(
                    surfaceTop - up * (thickness * 0.5f),
                    Quaternion.LookRotation(up));
                box.center = Vector3.zero;
                box.size = new Vector3(extentX, extentY, thickness);

            }
            else
            {
                proxy.transform.SetParent(anchor.transform, false);
                box.center = Vector3.zero;
                box.size = new Vector3(extentX, extentY, thickness);
            }

            created++;
            if (standable)
            {
                standableCount++;
                if ((anchor.Label & MRUKAnchor.SceneLabels.TABLE) != 0)
                    tableCount++;
            }
        }

        FishTankLog.Warn(
            $"Anchor physics: proxies={created} standable={standableCount} tables={tableCount} layer={LayerMask.LayerToName(layer)}");
        LogPhysicsProbeNearRoom(room, layer);
    }

    static string GetProxyLabelName(MRUKAnchor.SceneLabels label)
    {
        if ((label & MRUKAnchor.SceneLabels.TABLE) != 0) return "TABLE";
        if ((label & MRUKAnchor.SceneLabels.COUCH) != 0) return "COUCH";
        if ((label & MRUKAnchor.SceneLabels.FLOOR) != 0) return "FLOOR";
        if ((label & MRUKAnchor.SceneLabels.STORAGE) != 0) return "STORAGE";
        if ((label & MRUKAnchor.SceneLabels.WALL_FACE) != 0) return "WALL_FACE";
        return label.ToString();
    }

    void LogPhysicsProbeNearRoom(MRUKRoom room, int realWorldLayer)
    {
        if (room == null || Camera.main == null) return;

        Vector3 probe = Camera.main.transform.position + Vector3.down * 0.5f;
        int mask = 1 << realWorldLayer;
        bool hit = Physics.Raycast(probe, Vector3.down, out RaycastHit hitInfo, 2f, mask, QueryTriggerInteraction.Ignore);
        float mrukDist = room.TryGetClosestSurfacePosition(probe, out Vector3 surf, out MRUKAnchor _, out _);
        FishTankLog.Info($"Physics probe: unityRaycast={hit} hit={hitInfo.collider?.name} mrukDist={mrukDist:F3} surf={surf}");
    }

    static int CountAnchorPhysicsColliders(MRUKRoom room)
    {
        if (room?.Anchors == null) return 0;
        int n = 0;
        foreach (var anchor in room.Anchors)
        {
            if (anchor == null) continue;
            n += anchor.GetComponentsInChildren<Collider>(true).Length;
        }
        return n;
    }

    void ApplyBoundaryVisibilityPolicy()
    {
        var mgr = OVRManager.instance;
        if (mgr == null) return;
        try
        {
            mgr.shouldBoundaryVisibilityBeSuppressed = SuppressBoundaryVisibility;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MRUKBootstrap] Failed to apply boundary visibility policy: {e.Message}");
        }
    }

    void EnsureEffectMeshMaterial()
    {
        if (_effectMesh == null || _effectMesh.MeshMaterial != null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Lit")
                        ?? Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[MRUKBootstrap] No shader for EffectMesh.MeshMaterial; mesh/colliders may not spawn.");
            return;
        }

        var mat = new Material(shader);
        mat.color = new Color(1f, 1f, 1f, 0.02f);
        _effectMesh.MeshMaterial = mat;
    }

    void TryCreateEffectMeshForRoom(MRUKRoom room)
    {
        EnsureEffectMeshMaterial();
        _effectMesh.Labels = Labels;
        _effectMesh.Colliders = true;

        bool invokedRoomOverload = false;
        foreach (var m in typeof(EffectMesh).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (m.Name != nameof(EffectMesh.CreateMesh)) continue;
            var ps = m.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(MRUKRoom))
                continue;
            m.Invoke(_effectMesh, new object[] { room });
            invokedRoomOverload = true;
            break;
        }

        if (!invokedRoomOverload)
            _effectMesh.CreateMesh();

        ApplyEffectMeshPhysicsLayers();
        LogEffectMeshObjectSummary("after CreateMesh");
    }

    void ApplyEffectMeshPhysicsLayers()
    {
        if (_effectMesh == null) return;

        int layer = LayerMask.NameToLayer(RealWorldLayerName);
        if (layer < 0)
        {
            FishTankLog.Warn($"Layer '{RealWorldLayerName}' not found; EffectMesh colliders stay on Default.");
            return;
        }

        int fixedCount = 0;
        var dict = _effectMesh.EffectMeshObjects;
        if (dict == null) return;

        foreach (var kv in dict)
        {
            var wrapper = kv.Value;
            if (wrapper?.effectMeshGO == null) continue;

            foreach (var col in wrapper.effectMeshGO.GetComponentsInChildren<Collider>(true))
            {
                if (col == null) continue;
                col.gameObject.layer = layer;
                col.enabled = true;
                if (col.isTrigger) col.isTrigger = false;

                if (col is MeshCollider { convex: false } && col.attachedRigidbody == null)
                {
                    var rb = col.gameObject.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }

                fixedCount++;
            }
        }

        FishTankLog.Info($"EffectMesh physics layers applied: colliders={fixedCount} layer={RealWorldLayerName}");
    }

    static int CountEffectMeshColliders(EffectMesh effectMesh)
    {
        if (effectMesh == null) return 0;

        int fromObjects = 0;
        int meshObjects = 0;
        var dict = effectMesh.EffectMeshObjects;
        if (dict != null)
        {
            foreach (var kv in dict)
            {
                meshObjects++;
                var wrapper = kv.Value;
                if (wrapper == null) continue;

                if (wrapper.collider != null)
                {
                    fromObjects++;
                    continue;
                }

                if (wrapper.effectMeshGO != null)
                    fromObjects += wrapper.effectMeshGO.GetComponentsInChildren<Collider>(true).Length;
            }
        }

        if (fromObjects > 0)
            return fromObjects;

        // Fallback: colliders may live under MRUK hierarchy, not under EffectMesh host GO.
        return effectMesh.GetComponentsInChildren<Collider>(true).Length;
    }

    void LogEffectMeshObjectSummary(string context)
    {
        if (_effectMesh == null) return;
        int meshObjects = _effectMesh.EffectMeshObjects?.Count ?? 0;
        int colliders = CountEffectMeshColliders(_effectMesh);
        int onRealWorld = 0;
        int triggers = 0;
        if (_effectMesh.EffectMeshObjects != null)
        {
            int rw = LayerMask.NameToLayer(RealWorldLayerName);
            foreach (var kv in _effectMesh.EffectMeshObjects)
            {
                if (kv.Value?.effectMeshGO == null) continue;
                foreach (var col in kv.Value.effectMeshGO.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null) continue;
                    if (col.isTrigger) triggers++;
                    if (rw >= 0 && col.gameObject.layer == rw) onRealWorld++;
                }
            }
        }

        FishTankLog.Info($"EffectMesh ({context}): meshObjects={meshObjects} colliders={colliders} onRealWorld={onRealWorld} triggers={triggers}");
    }

    IEnumerator VerifyEffectMeshCollidersWhenReady(MRUKRoom room)
    {
        yield return null;
        yield return null;

        for (int i = 0; i < 45; i++)
        {
            int colliders = CountEffectMeshColliders(_effectMesh);
            if (colliders > 0)
            {
                ApplyEffectMeshPhysicsLayers();
                LogEffectMeshObjectSummary("ready");
                _effectMeshAuditRoutine = null;
                yield break;
            }

            if (i == 10)
            {
                FishTankLog.Warn("EffectMesh still 0 colliders; rebuilding mesh.");
                TryCreateEffectMeshForRoom(room);
            }

            yield return null;
        }

        LogEffectMeshObjectSummary("timeout");

        if (CreateAnchorPhysicsFallback && SupplementEffectMeshPhysics)
            EnsureAnchorPhysicsColliders(room);

        int anchorCols = CountAnchorPhysicsColliders(room);
        int effectCols = CountEffectMeshColliders(_effectMesh);
        FishTankLog.Warn($"Collider audit: effectMesh={effectCols} anchorFallback={anchorCols} anchors={CountAnchors(room)}");
        _effectMeshAuditRoutine = null;
    }

    IEnumerator CoRefreshEffectMeshAfterSceneReload()
    {
        for (int i = 0; i < 15; i++)
            yield return null;

        MRUKRoom room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room == null)
        {
            Debug.LogWarning("[MRUKBootstrap] Post-reload: GetCurrentRoom() null; waiting 2s.");
            yield return new WaitForSeconds(2f);
            room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        }

        if (room != null)
        {
            Debug.Log("[MRUKBootstrap] Post Space Setup / LoadSceneFromDevice: applying room.");
            HandleRoomReady(room);
        }
        else
            Debug.LogWarning("[MRUKBootstrap] Post-reload: still no MRUK room.");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_mruk != null)
        {
            _mruk.RoomCreatedEvent.RemoveListener(OnRoomCreated);
            _mruk.RoomUpdatedEvent.RemoveListener(OnRoomUpdated);
        }
    }
}

public class MRUKRescanInput : MonoBehaviour
{
    public float LongPinchDuration = 2f;
    public float TriggerCooldown = 3f;

    private float _pinchHoldSeconds;
    private float _lastTriggerTime = -999f;
    private OVRHand _leftHand;

    void Update()
    {
        if (Time.time - _lastTriggerTime < TriggerCooldown) return;

#if !UNITY_EDITOR
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch) ||
            OVRInput.GetDown(OVRInput.RawButton.Y))
        {
            Trigger("left Y button");
            return;
        }
#endif

        if (_leftHand == null)
        {
            foreach (var h in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
            {
                if (h != null && h.name != null && h.name.Contains("Left"))
                {
                    _leftHand = h;
                    break;
                }
            }
        }

        if (_leftHand != null && _leftHand.IsTracked &&
            _leftHand.GetFingerIsPinching(OVRHand.HandFinger.Middle))
        {
            _pinchHoldSeconds += Time.deltaTime;
            if (_pinchHoldSeconds >= LongPinchDuration)
            {
                Trigger("left-hand middle-finger long pinch");
            }
        }
        else
        {
            _pinchHoldSeconds = 0f;
        }
    }

    void Trigger(string source)
    {
        _lastTriggerTime = Time.time;
        _pinchHoldSeconds = 0f;
        Debug.Log($"[MRUKRescanInput] Rescan triggered by {source}.");
        if (MRUKBootstrap.Instance != null)
        {
            _ = MRUKBootstrap.Instance.RequestRescan();
        }
    }
}
