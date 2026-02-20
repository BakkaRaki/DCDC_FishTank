using UnityEngine;
using Fusion;

public class EnvironmentSystem : NetworkBehaviour
{
    public static EnvironmentSystem Instance { get; private set; }

    // --- 1. 去掉 OnChanged 参数，只保留纯净的 [Networked] ---
    [Networked]
    public float Temperature { get; set; } = 20.0f;

    [Networked]
    public float LightIntensity { get; set; } = 1.0f;

    // --- 2. 用于记录上一帧的数值，以便检测变化 ---
    private float _lastTemperature;
    private float _lastLightIntensity;

    private Light _sceneLight;
    private MeshRenderer _waterRenderer;
    private Material _waterMatInstance;
    private float _defaultAmbientIntensity;

    public override void Spawned()
    {
        Instance = this;
        _defaultAmbientIntensity = RenderSettings.ambientIntensity;

        _sceneLight = FindFirstObjectByType<Light>();

        GameObject waterObj = GameObject.Find("WaterVolume");
        if (waterObj != null)
        {
            _waterRenderer = waterObj.GetComponent<MeshRenderer>();
            if (_waterRenderer != null)
            {
                _waterMatInstance = _waterRenderer.material;
            }
        }

        // 初始化上一帧数据，强制更新一次画面
        _lastTemperature = -999f;
        UpdateVisuals();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    // --- 3. 使用 Render 每帧检查变化 (替代 OnChanged) ---
    public override void Render()
    {
        // 检测：如果现在的温度不等于上一次记录的温度
        if (Temperature != _lastTemperature || LightIntensity != _lastLightIntensity)
        {
            // 更新画面
            UpdateVisuals();

            // 记录新数值
            _lastTemperature = Temperature;
            _lastLightIntensity = LightIntensity;
        }
    }

    private void UpdateVisuals()
    {
        // A. 计算颜色
        float t = Mathf.InverseLerp(0, 40, Temperature);
        Color targetColor = Color.Lerp(new Color(0, 0.5f, 1f, 0.3f), new Color(1f, 0.2f, 0.2f, 0.4f), t);

        // B. 应用材质颜色
        if (_waterMatInstance != null)
        {
            bool hasBaseColor = _waterMatInstance.HasProperty("_BaseColor");
            bool hasColor = _waterMatInstance.HasProperty("_Color");

            if (hasBaseColor) _waterMatInstance.SetColor("_BaseColor", targetColor);
            else if (hasColor) _waterMatInstance.SetColor("_Color", targetColor);
        }

        // C. 应用光照
        if (_sceneLight != null)
        {
            _sceneLight.color = targetColor;
            _sceneLight.intensity = LightIntensity;
        }

        // D. 应用环境光
        RenderSettings.ambientIntensity = _defaultAmbientIntensity * LightIntensity;
        if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat)
        {
            RenderSettings.ambientLight = Color.gray * LightIntensity;
        }
    }

    // UI 接口
    public void SetTemperature(float val) { if (Object.HasStateAuthority) Temperature = val; else RPC_SetTemp(val); }
    public void SetLight(float val) { if (Object.HasStateAuthority) LightIntensity = val; else RPC_SetLight(val); }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_SetTemp(float v) { Temperature = v; }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_SetLight(float v) { LightIntensity = v; }
}