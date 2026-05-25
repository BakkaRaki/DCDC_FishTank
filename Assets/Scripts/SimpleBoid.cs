using UnityEngine;
using Fusion;
using Meta.XR.MRUtilityKit;

public class SimpleBoid : NetworkBehaviour
{
    [Header("Basic Settings")]
    public float Speed = 2.0f;
    public float RotationSpeed = 4.0f;

    [Header("Boids Weights")]
    public float SeparationWeight = 1.5f;
    public float AlignmentWeight = 1.0f;
    public float CohesionWeight = 1.0f;
    public float BoundsWeight = 1.2f;

    [Header("Perception")]
    public float VisionRadius = 1.5f;

    [Header("Interaction Weights")]
    public float FoodWeight = 2.0f;
    public float ScareWeight = 5.0f;
    public float ScareRadius = 0.8f;
    public float WallScareWeight = 7.0f;
    public float WallScareRadius = 0.25f;

    [Header("Food Approach")]
    public float FoodApproachRadius = 1.2f;
    public float FoodEatRadius = 0.35f;
    public float FoodCloseBoost = 2.0f;

    const float FlockSuppressMin = 0.2f;
    const float NoiseSuppressMin = 0.1f;
    const float NearFoodSpeedFactor = 0.4f;
    const float NearFoodRotationFactor = 2.25f;

    Vector3 _fallbackBoundsCenter = new Vector3(0, 1.5f, 1);
    float _fallbackBoundsRadius = 4f;
    float _randomOffset;

    public override void Spawned()
    {
        if (!AquariumManager.AllBoids.Contains(this))
            AquariumManager.AllBoids.Add(this);

        if (Object.HasStateAuthority)
        {
            _randomOffset = Random.Range(0f, 100f);
            Speed += Random.Range(-Speed * 0.2f, Speed * 0.2f);
            SeparationWeight += Random.Range(0.3f, 0.8f);
            CohesionWeight += Random.Range(-0.2f, 0.2f);
            VisionRadius += Random.Range(-0.5f, 0.5f);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (AquariumManager.AllBoids.Contains(this))
            AquariumManager.AllBoids.Remove(this);
    }

    public override void FixedUpdateNetwork()
    {
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

        float effectiveSpeed = Speed;
        var env = FindFirstObjectByType<EnvironmentSystem>();
        if (env != null)
        {
            float tempFactor = Mathf.InverseLerp(0, 40, env.Temperature);
            effectiveSpeed = Speed * (0.5f + tempFactor * 1.5f);
        }

        // --- Food attraction ---
        Vector3 foodSteer = Vector3.zero;
        float foodUrgency = 0f;

        GameObject[] foods = GameObject.FindGameObjectsWithTag("Food");
        GameObject closestFood = null;
        float minFoodDist = 100f;

        foreach (var f in foods)
        {
            float d = Vector3.Distance(transform.position, f.transform.position);
            if (d < minFoodDist && d < VisionRadius * 2f)
            {
                minFoodDist = d;
                closestFood = f;
            }
        }

        if (closestFood != null)
        {
            Vector3 toFood = closestFood.transform.position - transform.position;
            float distToFood = toFood.magnitude;

            if (distToFood < FoodEatRadius)
            {
                if (Object.HasStateAuthority)
                {
                    var foodNetObj = closestFood.GetComponent<NetworkObject>();
                    if (foodNetObj != null)
                        Runner.Despawn(foodNetObj);
                }
                return;
            }

            foodUrgency = 1f - Mathf.Clamp01(distToFood / FoodApproachRadius);
            if (toFood.sqrMagnitude > 1e-6f)
                foodSteer = toFood.normalized * (0.35f + foodUrgency * FoodCloseBoost);
        }

        // --- Player scare ---
        Vector3 scareSteer = Vector3.zero;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        foreach (var p in players)
        {
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < ScareRadius)
                scareSteer += (transform.position - p.transform.position).normalized / d;
        }

        Vector3 wallScare = MRSurfaceAvoidance.GetScareVector(transform.position, WallScareRadius);

        foreach (var other in AquariumManager.AllBoids)
        {
            if (other == this || other == null) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);

            if (dist < VisionRadius)
            {
                neighborCount++;

                if (dist < 0.5f)
                    separation += (transform.position - other.transform.position) / dist;

                alignment += other.transform.forward;
                averagePosition += other.transform.position;
            }
        }

        if (neighborCount > 0)
        {
            alignment /= neighborCount;
            averagePosition /= neighborCount;
            cohesion = averagePosition - transform.position;
        }

        var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room != null)
        {
            bool inRoom = room.IsPositionInRoom(transform.position, true);
            if (!inRoom)
            {
                float dist = room.TryGetClosestSurfacePosition(
                    transform.position,
                    out Vector3 surfPos,
                    out MRUKAnchor anchor,
                    out Vector3 normal);

                if (dist >= 0f)
                {
                    float strength = Mathf.Clamp01(dist / 0.5f);
                    boundsPull = normal.normalized * (1.0f + strength * 3.0f);
                }
            }
        }
        else
        {
            float distToBounds = Vector3.Distance(transform.position, _fallbackBoundsCenter);
            if (distToBounds > _fallbackBoundsRadius)
                boundsPull = (_fallbackBoundsCenter - transform.position) * (distToBounds - _fallbackBoundsRadius);
        }

        float flockScale = closestFood != null
            ? Mathf.Lerp(1f, FlockSuppressMin, foodUrgency)
            : 1f;
        float noiseScale = closestFood != null
            ? Mathf.Lerp(1f, NoiseSuppressMin, foodUrgency)
            : 1f;
        float wallScale = closestFood != null
            ? Mathf.Lerp(1f, 0.5f, foodUrgency)
            : 1f;
        float effectiveFoodWeight = FoodWeight * (closestFood != null ? 1f + foodUrgency : 1f);
        float effectiveRotation = RotationSpeed * (closestFood != null
            ? Mathf.Lerp(1f, NearFoodRotationFactor, foodUrgency)
            : 1f);

        if (closestFood != null)
            effectiveSpeed *= Mathf.Lerp(1f, NearFoodSpeedFactor, foodUrgency);

        Vector3 moveDirection = transform.forward;

        moveDirection += separation * SeparationWeight * flockScale;
        moveDirection += alignment * AlignmentWeight * flockScale;
        moveDirection += cohesion * CohesionWeight * flockScale;
        moveDirection += boundsPull * BoundsWeight;
        moveDirection += foodSteer * effectiveFoodWeight;
        moveDirection += scareSteer * ScareWeight;
        moveDirection += wallScare * (WallScareWeight * wallScale);

        float noiseX = Mathf.PerlinNoise(Time.time * 0.5f, _randomOffset) - 0.5f;
        float noiseY = Mathf.PerlinNoise(_randomOffset, Time.time * 0.5f) - 0.5f;
        float noiseZ = Mathf.PerlinNoise(Time.time * 0.5f, _randomOffset + 50f) - 0.5f;
        Vector3 noiseVector = new Vector3(noiseX, noiseY, noiseZ);
        moveDirection += noiseVector * (0.5f * noiseScale);

        if (moveDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Runner.DeltaTime * effectiveRotation);
        }

        transform.position += transform.forward * effectiveSpeed * Runner.DeltaTime;
    }
}
