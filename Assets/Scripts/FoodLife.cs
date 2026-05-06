using UnityEngine;
using Fusion;

public class FoodLife : NetworkBehaviour
{
    [Networked] private TickTimer Life { get; set; }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            Life = TickTimer.CreateFromSeconds(Runner, 10.0f);
        }

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.angularDamping = 2f;

            if (!Object.HasStateAuthority)
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            else
            {
                rb.isKinematic = false;
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        if (Life.Expired(Runner))
            Runner.Despawn(Object);
    }
}