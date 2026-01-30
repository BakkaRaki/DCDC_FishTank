using Fusion;
public class FoodLife : NetworkBehaviour
{
    [Networked] private TickTimer Life { get; set; }

    public override void Spawned()
    {
        Life = TickTimer.CreateFromSeconds(Runner, 10.0f); // 10√Î∫Ûœ˚ ß
    }

    public override void FixedUpdateNetwork()
    {
        if (Life.Expired(Runner))
            Runner.Despawn(Object);
    }
}