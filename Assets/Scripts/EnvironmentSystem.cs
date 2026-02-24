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
    // 在顶部增加变量
    public ParticleSystem WaterParticles;

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

        // 在 Spawned() 里查找
        if (WaterParticles == null)
        {
            GameObject pObj = GameObject.Find("WaterParticles");
            if (pObj != null) WaterParticles = pObj.GetComponent<ParticleSystem>();
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
        // 1. 计算基于温度的基础颜色 (0度蓝 -> 40度红)
        // 注意：Alpha (透明度) 设为 0.3f 左右
        float t = Mathf.InverseLerp(0, 40, Temperature);
        Color baseColor = Color.Lerp(new Color(0, 0.5f, 1f, 0.3f), new Color(1f, 0.2f, 0.2f, 0.3f), t);

        // 2. [关键修复] 将光照强度应用到颜色上
        // 也就是说：最终颜色 = 基础颜色 * 光照强度
        // 当 LightIntensity 为 0 时，finalColor 就会变成 (0,0,0,0) -> 完全看不见/黑色
        Color finalColor = baseColor * LightIntensity;

        // 保持 Alpha 值不要因为乘以 0 而完全消失（可选），或者让它跟着变黑
        // 如果你希望变暗时水体依然有“介质感”，可以单独处理 Alpha
        // finalColor.a = baseColor.a * (0.5f + 0.5f * LightIntensity); // 最暗也有 50% 透明度

        // 3. 应用颜色到水体材质
        if (_waterMatInstance != null)
        {
            bool hasBaseColor = _waterMatInstance.HasProperty("_BaseColor");
            bool hasColor = _waterMatInstance.HasProperty("_Color");

            if (hasBaseColor) _waterMatInstance.SetColor("_BaseColor", finalColor);
            else if (hasColor) _waterMatInstance.SetColor("_Color", finalColor);
        }

        // 4. 应用光照到灯光组件 (照亮鱼)
        if (_sceneLight != null)
        {
            // 灯光的颜色也应该随着变暗
            _sceneLight.color = finalColor;
            _sceneLight.intensity = LightIntensity;
        }

        // 5. 应用环境光 (让阴影部分也变黑)
        RenderSettings.ambientIntensity = _defaultAmbientIntensity * LightIntensity;
        if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat)
        {
            RenderSettings.ambientLight = Color.gray * LightIntensity;
        }

        //Particles getting dark
        if (WaterParticles != null)
        {
            var main = WaterParticles.main;
            // 修改粒子的 StartColor
            // 让粒子颜色也乘以光照强度
            Color particleColor = new Color(1, 1, 1, 0.5f) * LightIntensity;
            main.startColor = particleColor;
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