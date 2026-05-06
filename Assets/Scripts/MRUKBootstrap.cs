using System.Collections;
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
    public MRUKAnchor.SceneLabels Labels = (MRUKAnchor.SceneLabels)~0;

    [Header("Rescan")]
    public float FirstLaunchAutoScanDelay = 5f;
    public bool PromptForRoomScanOnStartup = true;
    public bool SuppressBoundaryVisibility = false;

    private MRUK _mruk;
    private EffectMesh _effectMesh;
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
            LoadSceneOnStartup = LoadSceneOnDevice
        };

#if UNITY_EDITOR
        if (EditorSceneJson != null)
        {
            settings.DataSource = MRUK.SceneDataSource.Json;
            settings.SceneJsons = new TextAsset[] { EditorSceneJson };
            settings.LoadSceneOnStartup = true;
        }
        else
        {
            settings.LoadSceneOnStartup = false;
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

            int layer = LayerMask.NameToLayer(RealWorldLayerName);
            if (layer >= 0)
            {
                _effectMesh.Layer = layer;
            }
        }

        _mruk.RoomCreatedEvent.AddListener(OnRoomCreated);
        _mruk.RoomUpdatedEvent.AddListener(OnRoomUpdated);

        if (GetComponent<MRUKRescanInput>() == null)
        {
            gameObject.AddComponent<MRUKRescanInput>();
        }

        ApplyBoundaryVisibilityPolicy();
    }

    void Start()
    {
#if !UNITY_EDITOR
        if (LoadSceneOnDevice && FirstLaunchAutoScanDelay > 0f)
        {
            StartCoroutine(FirstLaunchAutoScan());
        }
#endif
    }

    IEnumerator FirstLaunchAutoScan()
    {
        yield return new WaitForSeconds(FirstLaunchAutoScanDelay);

        if (_startupScanRequestedThisSession) yield break;

        bool shouldRequestScan = PromptForRoomScanOnStartup;
        var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room == null)
        {
            shouldRequestScan = true;
        }
        else if (Camera.main != null && !room.IsPositionInRoom(Camera.main.transform.position, true))
        {
            shouldRequestScan = true;
        }

        if (!shouldRequestScan) yield break;

        _startupScanRequestedThisSession = true;
        _ = RequestRescan();
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

    void OnRoomCreated(MRUKRoom room)
    {
        HandleRoomReady(room);
    }

    void OnRoomUpdated(MRUKRoom room)
    {
        HandleRoomReady(room);
    }

    void HandleRoomReady(MRUKRoom room)
    {
        if (_effectMesh != null && CreateEffectMeshColliders)
        {
            _effectMesh.CreateMesh();
        }

        ApplyBoundaryVisibilityPolicy();
        LastLoadedRoom = room;

        var handler = RoomReady;
        if (handler != null)
        {
            try { handler.Invoke(room); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
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
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// 在场景启动时初始化 Meta XR MRUK（房间理解）系统。
///
/// 两种使用方式：
///   A. 自动（默认）：什么都不用做。脚本会在第一个场景加载后自动创建自己 + MRUK + EffectMesh。
///   B. 手动：把本脚本挂到场景中某个 GameObject 上，可在 Inspector 调参。
///
/// 等价于添加 Building Block "MR Utility Kit"，但纯代码实现，便于版本管理与免场景编辑。
/// </summary>
public class MRUKBootstrap : MonoBehaviour
{
    /// <summary>
    /// Fired on the main thread after MRUK reports the first room is ready.
    /// Subscribers (e.g. FishTankVolume) can use this to place virtual content
    /// on real surfaces without requiring manual user input.
    /// If a subscriber attaches AFTER the room is already loaded, it will be
    /// invoked immediately with the cached LastLoadedRoom.
    /// </summary>
    public static event System.Action<MRUKRoom> RoomReady;

    public static MRUKRoom LastLoadedRoom { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        // 如果场景里已经有一个 Bootstrap（手动挂了），就不再自动创建
        if (FindAnyObjectByType<MRUKBootstrap>() != null) return;
        var go = new GameObject("[Auto] MRUKBootstrap");
        DontDestroyOnLoad(go);
        go.AddComponent<MRUKBootstrap>();
    }

    /// <summary>
    /// Helper so late subscribers still get a notification if the room is
    /// already loaded by the time they spawn (common on clients that join
    /// after Host has finished scene loading).
    /// </summary>
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
    [Tooltip("真机：启动时请求 Scene 权限并加载房间数据。")]
    public bool LoadSceneOnDevice = true;

    [Tooltip("Editor / PC 调试：加载一个假房间 Json 方便测试（可留空，留空则 Editor 里不加载）。")]
    public TextAsset EditorSceneJson;

    [Header("EffectMesh (隐形碰撞体)")]
    [Tooltip("给真实房间表面生成隐形 MeshCollider / BoxCollider，让食物能碰撞真实桌面/墙/地板。")]
    public bool CreateEffectMeshColliders = true;

    [Tooltip("RealWorld 层名字（必须在 TagManager 里已经添加）。")]
    public string RealWorldLayerName = "RealWorld";

    [Tooltip("对哪些房间标签生成碰撞体。默认 All。")]
    public MRUKAnchor.SceneLabels Labels = (MRUKAnchor.SceneLabels)~0;

    [Header("Rescan (switch physical rooms)")]
    [Tooltip("If no room is loaded this many seconds after boot, automatically launch OVRScene.RequestSpaceSetup to ask the user to scan the current room.")]
    public float FirstLaunchAutoScanDelay = 5f;

    [Tooltip("Prompt Space Setup once each app launch so the user can confirm/scan the current physical room instead of silently using stale cached scene data.")]
    public bool PromptForRoomScanOnStartup = true;

    [Tooltip("Keep Guardian/Boundary visible/enabled. Scene API and Space Setup need the platform boundary to stay active.")]
    public bool SuppressBoundaryVisibility = false;

    private MRUK _mruk;
    private EffectMesh _effectMesh;
    private bool _rescanInFlight;
    private static bool _startupScanRequestedThisSession;
    public static MRUKBootstrap Instance { get; private set; }

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
            Debug.Log("[MRUKBootstrap] Using existing MRUK instance.");
        }

        var settings = new MRUK.MRUKSettings
        {
            DataSource = MRUK.SceneDataSource.Device,
            LoadSceneOnStartup = LoadSceneOnDevice
        };

#if UNITY_EDITOR
        if (EditorSceneJson != null)
        {
            settings.DataSource = MRUK.SceneDataSource.Json;
            settings.SceneJsons = new TextAsset[] { EditorSceneJson };
            settings.LoadSceneOnStartup = true;
        }
        else
        {
            settings.LoadSceneOnStartup = false;
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

            int layer = LayerMask.NameToLayer(RealWorldLayerName);
            if (layer >= 0)
            {
                _effectMesh.Layer = layer;
            }
            else
            {
                Debug.LogWarning($"[MRUKBootstrap] Layer '{RealWorldLayerName}' not found, using Default.");
            }
        }

        _mruk.RoomCreatedEvent.AddListener(OnRoomCreated);
        _mruk.RoomUpdatedEvent.AddListener(OnRoomUpdated);
        Debug.Log("[MRUKBootstrap] Initialized. Waiting for room data...");

        if (GetComponent<MRUKRescanInput>() == null)
        {
            gameObject.AddComponent<MRUKRescanInput>();
        }

        ApplyBoundaryVisibilityPolicy();
    }

    void Start()
    {
#if !UNITY_EDITOR
        if (LoadSceneOnDevice && FirstLaunchAutoScanDelay > 0f)
        {
            StartCoroutine(FirstLaunchAutoScan());
        }
#endif
    }

    IEnumerator FirstLaunchAutoScan()
    {
        // Give MRUK a chance to finish its startup LoadSceneFromDevice first.
        yield return new WaitForSeconds(FirstLaunchAutoScanDelay);

        if (_startupScanRequestedThisSession)
        {
            yield break;
        }

        bool shouldRequestScan = PromptForRoomScanOnStartup;
        var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room == null)
        {
            shouldRequestScan = true;
        }
        else if (Camera.main != null && !room.IsPositionInRoom(Camera.main.transform.position, true))
        {
            shouldRequestScan = true;
            Debug.Log("[MRUKBootstrap] Current head position is outside loaded MRUK room. Requesting Space Setup.");
        }

        if (!shouldRequestScan)
        {
            yield break;
        }

        _startupScanRequestedThisSession = true;
        Debug.Log("[MRUKBootstrap] Launching startup Space Setup so the user can scan/confirm the current room.");
        _ = RequestRescan();
    }

    /// <summary>
    /// Force the user to (re)capture their current physical space.
    /// Launches Meta's Space Setup overlay and then reloads MRUK from the
    /// updated device scene data. Safe to call at any time; the method is
    /// re-entrant-guarded so concurrent presses are ignored.
    /// </summary>
    public async Task RequestRescan()
    {
        if (_rescanInFlight)
        {
            Debug.Log("[MRUKBootstrap] Rescan already in progress, ignoring new request.");
            return;
        }
        _rescanInFlight = true;
        try
        {
#if UNITY_EDITOR
            Debug.Log("[MRUKBootstrap] RequestRescan is a no-op in the Editor.");
            await Task.Yield();
            return;
#else
            Debug.Log("[MRUKBootstrap] Launching OVRScene.RequestSpaceSetup...");
            bool captured = await OVRScene.RequestSpaceSetup();
            Debug.Log($"[MRUKBootstrap] Space Setup returned: {captured}");

            if (MRUK.Instance == null)
            {
                Debug.LogWarning("[MRUKBootstrap] MRUK.Instance is null after Space Setup; cannot reload.");
                return;
            }

            // Reload fresh scene data; RoomCreatedEvent will fire our OnRoomCreated
            // which rebuilds EffectMesh colliders and re-invokes RoomReady so
            // FishTankVolume can auto-place itself in the new room.
            await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: true, removeMissingRooms: true);
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

    void OnRoomCreated(MRUKRoom room)
    {
        Debug.Log($"[MRUKBootstrap] Room loaded. Anchors: {room.Anchors.Count}");
        HandleRoomReady(room);
    }

    void OnRoomUpdated(MRUKRoom room)
    {
        Debug.Log($"[MRUKBootstrap] Room updated. Anchors: {room.Anchors.Count}");
        HandleRoomReady(room);
    }

    void HandleRoomReady(MRUKRoom room)
    {

        if (_effectMesh != null && CreateEffectMeshColliders)
        {
            _effectMesh.CreateMesh();
            Debug.Log("[MRUKBootstrap] Real-world colliders generated.");
        }

        ApplyBoundaryVisibilityPolicy();

        LastLoadedRoom = room;
        var handler = RoomReady;
        if (handler != null)
        {
            try { handler.Invoke(room); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
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

/// <summary>
/// Listens for a user "rescan" trigger and asks <see cref="MRUKBootstrap"/>
/// to re-run Meta's Space Setup. Triggers:
///   - Left controller Y button (OVRInput.Button.Two on LTouch)
///   - Left-hand middle-finger pinch held for <see cref="LongPinchDuration"/> seconds (fallback for hand-tracking users who have no controller in hand)
/// Automatically added by <see cref="MRUKBootstrap"/>; no manual setup required.
/// </summary>
public class MRUKRescanInput : MonoBehaviour
{
    [Tooltip("Seconds the left-hand middle-finger pinch must be held to trigger a rescan.")]
    public float LongPinchDuration = 2f;

    [Tooltip("Cooldown after a successful trigger so the user can release the gesture / button without retriggering.")]
    public float TriggerCooldown = 3f;

    private float _pinchHoldSeconds;
    private float _lastTriggerTime = -999f;
    private OVRHand _leftHand;

    void Update()
    {
        if (Time.time - _lastTriggerTime < TriggerCooldown) return;

#if !UNITY_EDITOR
        // Y button on left Touch controller.
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
