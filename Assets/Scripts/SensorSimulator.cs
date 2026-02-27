using UnityEngine;
using Fusion;
using System.Collections;

public class SensorSimulator : NetworkBehaviour
{
    [Header("Simulation Settings")]
    public float Interval = 10.0f; // 变化间隔 (秒)

    [Header("Temperature Constraints")]
    public float TempFluctuation = 2.0f; // 每次最大变化幅度 (+/- 2度)
    public Vector2 TempLimits = new Vector2(18.0f, 30.0f); // 模拟真实鱼缸的适宜温度范围

    [Header("Light Constraints")]
    public float LightFluctuation = 0.1f; // 每次最大变化幅度
    public Vector2 LightLimits = new Vector2(0.2f, 1.2f); // 光照范围

    public override void Spawned()
    {
        // 只有 Host (权威端) 负责运行传感器模拟
        // Client 不需要跑这个，它们只需要同步结果
        if (Object.HasStateAuthority)
        {
            StartCoroutine(SensorLoop());
        }
    }

    IEnumerator SensorLoop()
    {
        // 等待一小会儿再开始，确保 EnvironmentSystem 已初始化
        yield return new WaitForSeconds(1.0f);

        while (true)
        {
            yield return new WaitForSeconds(Interval);

            if (EnvironmentSystem.Instance != null)
            {
                SimulateReadings();
            }
        }
    }

    void SimulateReadings()
    {
        // 1. 获取当前值
        float currentTemp = EnvironmentSystem.Instance.Temperature;
        float currentLight = EnvironmentSystem.Instance.LightIntensity;

        // 2. 计算新温度 (随机波动)
        // Random.Range(-2, 2) -> 比如 +1.5 或 -0.8
        float deltaTemp = Random.Range(-TempFluctuation, TempFluctuation);
        float newTemp = Mathf.Clamp(currentTemp + deltaTemp, TempLimits.x, TempLimits.y);

        // 3. 计算新光照
        float deltaLight = Random.Range(-LightFluctuation, LightFluctuation);
        float newLight = Mathf.Clamp(currentLight + deltaLight, LightLimits.x, LightLimits.y);

        // 4. 应用回系统 (直接修改 Networked 变量)
        EnvironmentSystem.Instance.Temperature = newTemp;
        EnvironmentSystem.Instance.LightIntensity = newLight;

        Debug.Log($"[Sensor] Auto-Update: Temp {currentTemp:F1}->{newTemp:F1}, Light {currentLight:F2}->{newLight:F2}");
    }
}