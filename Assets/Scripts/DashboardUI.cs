using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DashboardUI : MonoBehaviour
{
    [Header("UI Components")]
    public Slider TempSlider;
    public TextMeshProUGUI TempText;

    public Slider LightSlider;
    public TextMeshProUGUI LightText;

    // 防止自己拖动时产生“回弹”抖动
    private bool _isDragging = false;

    void Start()
    {
        // 监听滑块拖动事件
        TempSlider.onValueChanged.AddListener(OnTempChanged);
        LightSlider.onValueChanged.AddListener(OnLightChanged);
    }

    void Update()
    {
        // 必须等 EnvironmentSystem 生成了才能工作
        if (EnvironmentSystem.Instance == null) return;

        // --- 1. 更新数值显示 (Text) ---
        // 实时显示当前的某些数值，这体现了 Twin 的监控特性
        float currentTemp = EnvironmentSystem.Instance.Temperature;
        float currentLight = EnvironmentSystem.Instance.LightIntensity;

        TempText.text = $"Temp: {currentTemp:F1} °C";
        LightText.text = $"Light: {currentLight:F1}";

        // --- 2. 更新滑块位置 (Slider) ---
        // 只有当我没有在拖动时，才去同步网络值。
        // 否则网络值和我手的位置打架，滑块会抽搐。
        // (注：简单的做法是直接 SetValueWithoutNotify，但为了体验更好，我们可以加个阈值判断)

        if (Mathf.Abs(TempSlider.value - currentTemp) > 0.1f)
        {
            TempSlider.SetValueWithoutNotify(currentTemp);
        }

        if (Mathf.Abs(LightSlider.value - currentLight) > 0.1f)
        {
            LightSlider.SetValueWithoutNotify(currentLight);
        }
    }

    // 当我拖动温度滑块时调用
    void OnTempChanged(float val)
    {
        if (EnvironmentSystem.Instance != null)
        {
            EnvironmentSystem.Instance.SetTemperature(val);
        }
    }

    // 当我拖动光照滑块时调用
    void OnLightChanged(float val)
    {
        if (EnvironmentSystem.Instance != null)
        {
            EnvironmentSystem.Instance.SetLight(val);
        }
    }
}