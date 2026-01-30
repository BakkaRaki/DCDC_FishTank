using UnityEngine;
using Fusion;
using System.Collections.Generic;

public class SimpleBoid : NetworkBehaviour
{
    [Header("Basic Settings")]
    public float Speed = 2.0f;
    public float RotationSpeed = 4.0f;

    [Header("Boids Weights (权重)")]
    public float SeparationWeight = 1.5f; // 分离权重 (最重要，防止穿模)
    public float AlignmentWeight = 1.0f;  // 对齐权重
    public float CohesionWeight = 1.0f;   // 聚集权重
    public float BoundsWeight = 1.2f;     // 回家权重

    [Header("Perception (感知)")]
    public float VisionRadius = 1.5f;     // 能看到多远的邻居

    [Header("Interaction Weights")]
    public float FoodWeight = 2.0f;      // 对食物的渴望
    public float ScareWeight = 5.0f;     // 对手的恐惧
    public float ScareRadius = 0.8f;     // 躲避范围

    // 鱼缸中心设置 (Day 2 的参数)
    private Vector3 _boundsCenter = new Vector3(0, 1.5f, 1);
    private float _boundsRadius = 5f;
    // 新增一个变量用于记录随机种子
    private float _randomOffset;

    // 当鱼出生时，把自己加入全局名单
    public override void Spawned()
    {
        // 1. 注册到全局名单 (原逻辑)
        if (!AquariumManager.AllBoids.Contains(this))
        {
            AquariumManager.AllBoids.Add(this);
        }

        // 2. [新增] 只有 Host 需要设置参数，Client 同步位置即可
        if (Object.HasStateAuthority)
        {
            // 随机种子
            _randomOffset = Random.Range(0f, 100f);

            // A. 速度差异：让有的鱼快，有的鱼慢 (±20% 浮动)
            Speed += Random.Range(-Speed * 0.2f, Speed * 0.2f);

            // B. 性格差异：
            // 增加分离权重的随机性，这最能打散队形
            SeparationWeight += Random.Range(0.5f, 1.5f);

            // 稍微随机化聚集权重
            CohesionWeight += Random.Range(-0.2f, 0.2f);

            // 随机感知范围 (有的近视，有的远视)
            VisionRadius += Random.Range(-0.5f, 0.5f);
        }
    }


    // 当鱼销毁时，把自己移除
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (AquariumManager.AllBoids.Contains(this))
        {
            AquariumManager.AllBoids.Remove(this);
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 依然只在 Host 计算，Client 只负责同步位置
        if (!Object.HasStateAuthority) return;

        CalculateFlocking();
    }

    void CalculateFlocking()
    {
        Vector3 separation = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        Vector3 cohesion = Vector3.zero;
        Vector3 boundsPull = Vector3.zero;

        int neighborCount = 0;
        Vector3 averagePosition = Vector3.zero;

        // --- 新增逻辑 1：寻找食物 (Attraction) ---
        Vector3 foodSteer = Vector3.zero;
        // 简单粗暴：找场景里所有的 Food
        // (优化建议：实际项目中应该用 Physics.OverlapSphere)
        GameObject[] foods = GameObject.FindGameObjectsWithTag("Food");
        GameObject closestFood = null;
        float minFoodDist = 100f;

        foreach (var f in foods)
        {
            float d = Vector3.Distance(transform.position, f.transform.position);
            if (d < minFoodDist && d < VisionRadius * 2) // 视野比看同伴远一点
            {
                minFoodDist = d;
                closestFood = f;
            }
        }

        if (closestFood != null)
        {
            float distToFood = Vector3.Distance(transform.position, closestFood.transform.position);

            // 1. [新增] 进食判定：如果距离非常近 (比如 < 0.2米)
            if (distToFood < 0.2f)
            {
                // 只有 Host 有权销毁物体
                if (Object.HasStateAuthority)
                {
                    // 获取食物身上的 NetworkObject
                    var foodNetObj = closestFood.GetComponent<NetworkObject>();
                    if (foodNetObj != null)
                    {
                        // 吃掉它！(销毁)
                        Runner.Despawn(foodNetObj);
                    }
                }
                // 既然吃掉了，这帧就不需要转向了
                return;
            }

            // 2. 原有的转向逻辑 (稍微减小一点权重，防止冲太猛)
            foodSteer = (closestFood.transform.position - transform.position).normalized;
            foodSteer *= Mathf.Clamp01(distToFood); 
        }

        // --- 新增逻辑 2：躲避玩家 (Repulsion) ---
        Vector3 scareSteer = Vector3.zero;
        // 遍历所有玩家 (Fusion 的 Player 列表很难直接拿位置，我们简单找 Tag 为 Player 的物体)
        // 假设你的 PlayerAvatar Tag 设为了 "Player"
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        foreach (var p in players)
        {
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < ScareRadius)
            {
                // 产生一个背离玩家的向量 (斥力)
                // 距离越近，斥力越大 (1.0/d)
                scareSteer += (transform.position - p.transform.position).normalized / d;
            }
        }
        // 鱼群逻辑
        // 1. 遍历所有鱼，寻找邻居
        foreach (var other in AquariumManager.AllBoids)
        {
            // 跳过自己 或 空物体
            if (other == this || other == null) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);

            // 如果在感知范围内
            if (dist < VisionRadius)
            {
                neighborCount++;

                // A. 分离：如果太近，就累加反向向量 (距离越近，斥力越大)
                if (dist < 0.5f)
                {
                    separation += (transform.position - other.transform.position) / dist;
                }

                // B. 对齐：累加邻居的朝向
                alignment += other.transform.forward;

                // C. 聚集：累加邻居的位置
                averagePosition += other.transform.position;
            }
        }

        // 2. 计算平均值
        if (neighborCount > 0)
        {
            alignment /= neighborCount;

            averagePosition /= neighborCount;
            // 聚集向量 = 邻居中心点 - 我当前位置
            cohesion = (averagePosition - transform.position);
        }

        // 3. 边界限制 (回家)
        float distToBounds = Vector3.Distance(transform.position, _boundsCenter);
        if (distToBounds > _boundsRadius)
        {
            // 游出去了，这就产生一个指向中心的强力
            boundsPull = (_boundsCenter - transform.position) * (distToBounds - _boundsRadius);
        }

        // 4. 最终决策：合成所有向量
        Vector3 moveDirection = transform.forward; // 保持惯性

        moveDirection += separation * SeparationWeight;
        moveDirection += alignment * AlignmentWeight;
        moveDirection += cohesion * CohesionWeight;
        moveDirection += boundsPull * BoundsWeight; 
        moveDirection += foodSteer * FoodWeight;
        moveDirection += scareSteer * ScareWeight; // 恐惧优先级最高

        // [新增] 加入柏林噪声 (Perlin Noise) 扰动
        // 基于时间和每条鱼唯一的 _randomOffset
        float noiseX = Mathf.PerlinNoise(Time.time * 0.5f, _randomOffset) - 0.5f;
        float noiseY = Mathf.PerlinNoise(_randomOffset, Time.time * 0.5f) - 0.5f;
        float noiseZ = Mathf.PerlinNoise(Time.time * 0.5f, _randomOffset + 50f) - 0.5f;

        // 给最终方向加一个细微的随机推力 (权重设为 0.5f 左右)
        Vector3 noiseVector = new Vector3(noiseX, noiseY, noiseZ);
        moveDirection += noiseVector * 0.5f;

        // 5. 应用移动
        if (moveDirection != Vector3.zero)
        {
            // 平滑旋转朝向目标方向
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Runner.DeltaTime * RotationSpeed);
        }

        // 向前游
        transform.position += transform.forward * Speed * Runner.DeltaTime;
    }
}