using UnityEngine;
using Fusion;

/// <summary>
/// Food uses MRUK kinematic fall — Unity physics against EffectMesh is unreliable on Quest.
/// </summary>
public class FoodLife : NetworkBehaviour
{
    [SerializeField] float surfaceSkin = MRFoodPhysics.DefaultSurfaceSkin;
    [SerializeField] float gravityScale = MRFoodPhysics.DefaultGravityScale;
    [SerializeField] float lifeSeconds = 30f;

    [Networked] private TickTimer Life { get; set; }

    Vector3 _velocity;
    NetworkTransform _networkTransform;
    int _debugLogCooldown;
    int _ticksSinceSpawn;
    const int MinTicksBeforeRest = 6;

    public override void Spawned()
    {
        MRFoodPhysics.EnsureFoodCollidesWithRealWorld();
        _networkTransform = GetComponent<NetworkTransform>();

        if (Object.HasStateAuthority)
            Life = TickTimer.CreateFromSeconds(Runner, lifeSeconds);

        var col = GetComponent<Collider>();
        if (col != null)
            col.enabled = false;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (!Object.HasStateAuthority) return;

        var bootstrap = FindFirstObjectByType<MRUKBootstrap>();
        if (bootstrap != null)
            bootstrap.RefreshFoodSupportPhysics();

        _ticksSinceSpawn = 0;
        _velocity = Vector3.zero;
        ApplyPosition(transform.position, hardSync: true);
        LogSupportOnce("spawn", transform.position);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        _ticksSinceSpawn++;

        Vector3 pos = transform.position;
        MRFoodPhysics.SimulateKinematicFall(ref pos, ref _velocity, Runner.DeltaTime, gravityScale, surfaceSkin,
            _ticksSinceSpawn, MinTicksBeforeRest);

        if (_ticksSinceSpawn >= MinTicksBeforeRest && Mathf.Abs(_velocity.y) < 0.01f &&
            MRFoodPhysics.TryFindSupportBelow(pos, 3f, surfaceSkin, out Vector3 support, out Vector3 normal))
        {
            float restY = support.y + normal.y * MRFoodPhysics.GetRestOffset(surfaceSkin);
            if (pos.y <= restY + 0.05f)
            {
                pos = support + normal * MRFoodPhysics.GetRestOffset(surfaceSkin);
                _velocity = Vector3.zero;
            }
        }

        ApplyPosition(pos, hardSync: false);

        if (_debugLogCooldown <= 0)
        {
            LogSupportOnce("tick", pos);
            _debugLogCooldown = 64;
        }
        else
        {
            _debugLogCooldown--;
        }

        if (Life.Expired(Runner))
            Runner.Despawn(Object);
    }

    void ApplyPosition(Vector3 pos, bool hardSync)
    {
        transform.position = pos;
        if (_networkTransform == null) return;
        if (hardSync || Mathf.Abs(_velocity.y) < 0.01f)
            _networkTransform.Teleport(pos, transform.rotation);
    }

    void LogSupportOnce(string phase, Vector3 pos)
    {
        if (MRFoodPhysics.TryFindSupportBelow(pos, 3f, out Vector3 sup, out Vector3 n))
        {
            float restY = sup.y + n.y * MRFoodPhysics.GetRestOffset(surfaceSkin);
            FishTankLog.Warn($"Food {phase} pos={pos} support={sup} restY={restY:F2} vy={_velocity.y:F2}");
        }
        else
        {
            var room = MRFoodPhysics.ResolveRoom();
            string cache = MRFoodPhysics.HasRoomCache
                ? $"floorY={MRFoodPhysics.CachedFloorY:F2}"
                : "no room cache";
            int anchors = room?.Anchors != null ? room.Anchors.Count : 0;
            FishTankLog.Warn($"Food {phase} pos={pos} NO support (anchors={anchors} {cache}) vy={_velocity.y:F2}");
        }
    }
}
