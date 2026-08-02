using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A delayed area explosion: after a short fuse it damages every unit inside its radius, then
/// returns itself to the pool.
///
/// It isn't a Unit — nothing can shoot it, and it has no health. It's a timer with a radius.
/// </summary>
public partial class BombBlast : SpokeNode2D {

    /// <summary>Blast radius in metres.</summary>
    [Export] public float Radius { get; set; } = 1.2f;

    /// <summary>Damage dealt to everything caught in it, in hit points.</summary>
    [Export] public float Damage { get; set; } = 0.5f;

    /// <summary>Fuse length in seconds.</summary>
    [Export] public float Duration { get; set; } = 0.3f;

    double elapsed;

    protected override void Init(EffectBuilder s) {
        ZIndex = 20;

        // Its whole life is one window in the tree, so a pooled reuse restarts the fuse from zero
        // by re-running this block. There is nothing else to reset.
        s.Phase(IsInTree, s => {
            elapsed = 0.0;

            s.OnProcess(delta => {
                elapsed += delta;
                QueueRedraw();
            });

            s.Wait(Duration, () => {
                foreach (var hit in GameState.GroundZone.Query(new Circle(GlobalPosition, World.Px(Radius)))) {
                    hit.Owner.Health.Damage(Damage);
                }
                Pool.Despawn(this);
            });
        });
    }

    public override void _Draw() {
        var r = World.Px(Radius);
        var t = Mathf.Min(1f, (float)(elapsed / Duration));
        DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 48, new Color(Palette.BlastRing, 0.35f), 2f, true);
        DrawArc(Vector2.Zero, r * t, 0f, Mathf.Tau, 48, new Color(Palette.BlastRing, 0.9f), 3.5f, true);
    }
}
