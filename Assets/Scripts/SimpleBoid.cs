using UnityEngine;
using Fusion;

public class SimpleBoid : NetworkBehaviour
{
    [Header("Boid Settings")]
    public float Speed = 2.0f;
    public float RotationSpeed = 5.0f;

    // 鱼群活动的边界（假设是一个 5x5x5 的鱼缸）
    private Vector3 _boundsCenter = new Vector3(0, 1.5f, 1);
    private float _boundsRadius = 3.0f;

    // 随机种子，让每条鱼动得不一样
    private float _noiseOffset;

    public override void Spawned()
    {
        _noiseOffset = Random.Range(0f, 100f);
    }

    // FixedUpdateNetwork 是 Fusion 专用的物理/逻辑更新帧
    public override void FixedUpdateNetwork()
    {
        // [权威性检查]：这行代码保证了以下逻辑只在 Host 运行！
        // Client 端压根不执行这里，完全依赖 NetworkTransform 同步位置。
        if (!Object.HasStateAuthority) return;

        MoveFish();
        KeepInBounds();
    }

    void MoveFish()
    {
        // 1. 简单的向前游动
        transform.position += transform.forward * Speed * Runner.DeltaTime;

        // 2. 模拟自然摆动 (Perlin Noise)
        float noise = Mathf.PerlinNoise(Time.time * 0.5f, _noiseOffset) - 0.5f;
        transform.Rotate(Vector3.up, noise * RotationSpeed * Runner.DeltaTime * 50f);

        // 3. 稍微带点上下起伏
        float verticalNoise = Mathf.PerlinNoise(_noiseOffset, Time.time * 0.5f) - 0.5f;
        transform.Rotate(Vector3.right, verticalNoise * Runner.DeltaTime * 20f);
    }

    // 简单的边界限制：如果游太远，就强制掉头转向中心
    void KeepInBounds()
    {
        float dist = Vector3.Distance(transform.position, _boundsCenter);
        if (dist > _boundsRadius)
        {
            Vector3 directionToCenter = (_boundsCenter - transform.position).normalized;
            // 平滑转向中心
            Quaternion targetRotation = Quaternion.LookRotation(directionToCenter);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Runner.DeltaTime * RotationSpeed);
        }
    }
}