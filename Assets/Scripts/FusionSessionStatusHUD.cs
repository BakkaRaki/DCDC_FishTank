using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space HUD for Quest testing (ASCII labels — LiberationSans has no CJK glyphs).
/// </summary>
public class FusionSessionStatusHUD : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] bool followMainCamera = true;
    [SerializeField] Vector3 localOffset = new Vector3(-0.22f, -0.12f, 0.55f);
    [SerializeField] Vector2 panelSize = new Vector2(420f, 200f);
    [SerializeField] float worldScale = 0.0012f;
    [SerializeField] int fontSize = 22;

    [Header("Font (optional — defaults to TMP Settings)")]
    [SerializeField] TMP_FontAsset fontAsset;

    TextMeshProUGUI _statusText;
    RectTransform _panelRect;
    float _refreshTimer;

    const float RefreshInterval = 0.25f;

    void Start()
    {
        EnsureHud();
        RefreshNow();
    }

    void LateUpdate()
    {
        if (_panelRect == null) return;

        if (followMainCamera && Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _panelRect.position = cam.position + cam.rotation * localOffset;
            _panelRect.rotation = Quaternion.LookRotation(_panelRect.position - cam.position, Vector3.up);
        }

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = RefreshInterval;
            RefreshNow();
        }
    }

    void EnsureHud()
    {
        if (_statusText != null) return;

        var root = new GameObject("SessionStatusHUD");
        root.transform.SetParent(transform, false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        root.AddComponent<GraphicRaycaster>();

        _panelRect = root.GetComponent<RectTransform>();
        _panelRect.sizeDelta = panelSize;
        _panelRect.localScale = Vector3.one * worldScale;

        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(root.transform, false);
        var bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var bg = bgGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);

        var textGo = new GameObject("StatusText");
        textGo.transform.SetParent(root.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 12f);
        textRect.offsetMax = new Vector2(-12f, -12f);

        _statusText = textGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(_statusText);
        _statusText.fontSize = fontSize;
        _statusText.color = Color.white;
        _statusText.alignment = TextAlignmentOptions.TopLeft;
        _statusText.enableWordWrapping = true;
        _statusText.richText = true;
    }

    void ApplyFont(TextMeshProUGUI text)
    {
        if (fontAsset != null)
        {
            text.font = fontAsset;
            return;
        }

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        if (TMP_Settings.fallbackFontAssets != null && TMP_Settings.fallbackFontAssets.Count > 0)
            text.fontSharedMaterial = text.font != null ? text.font.material : null;
    }

    void RefreshNow()
    {
        if (_statusText == null) return;

        NetworkRunner runner = FindRunner();
        if (runner == null || !runner.IsRunning)
        {
            _statusText.text =
                "<b>NETWORK</b>\n" +
                "Status: Offline\n" +
                "Tap Start Host or Join Client";
            return;
        }

        string role = runner.IsServer ? "Host" : runner.IsClient ? "Client" : "?";
        int playerCount = CountActivePlayers(runner);
        string spatial = GetSpatialStatusLine();
        string authority = GetLocalAuthorityLine();

        _statusText.text =
            "<b>NETWORK</b>\n" +
            $"Room: {AquariumSessionConfig.FusionSessionName}\n" +
            $"Role: {role}\n" +
            $"<size=120%><b>Players: {playerCount}</b></size>\n" +
            (playerCount >= 2
                ? "<color=#88FF88>2+ devices — test Colocation</color>\n"
                : "<color=#AAAAAA>Waiting for 2nd device…</color>\n") +
            $"Spatial: {spatial}\n" +
            $"Authority: {authority}";
    }

    static string GetSpatialStatusLine()
    {
        if (AquariumColocationGate.IsReady)
            return "<color=#88FF88>Aligned</color>";

        return "<color=#FFCC66>Waiting…</color>";
    }

    static string GetLocalAuthorityLine()
    {
        // Helps debug: if no local input-authority avatar exists, RPCs won't fire.
        int inputOwned = 0;
        var avatars = FindObjectsByType<AvatarMovement>(FindObjectsSortMode.None);
        foreach (var av in avatars)
        {
            if (av != null && av.Object != null && av.Object.HasInputAuthority)
                inputOwned++;
        }

        return inputOwned > 0 ? $"<color=#88FF88>InputAuthority x{inputOwned}</color>" : "<color=#FF6666>None</color>";
    }

    static NetworkRunner FindRunner() => FindAnyObjectByType<NetworkRunner>();

    static int CountActivePlayers(NetworkRunner runner)
    {
        int count = 0;
        foreach (var _ in runner.ActivePlayers)
            count++;
        return count;
    }
}
