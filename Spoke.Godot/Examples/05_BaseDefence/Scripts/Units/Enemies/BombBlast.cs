using Godot;

namespace Spoke.Examples.BaseDefence;

// A delayed area explosion: after a short fuse, damages every building in its radius, then despawns.
public partial class BombBlast : SpokeNode2D {

    [Export] public float Radius { get; set; } = 1.2f;
    [Export] public float Damage { get; set; } = 0.5f;
    [Export] public float Duration { get; set; } = 0.3f;

    double elapsed;

    protected override void Init(EffectBuilder s) {
        ZIndex = 20;

        // Its whole life is one enabled window, so a pooled reuse restarts the fuse
        // from zero by re-running this block. There's nothing else to reset.
        var isActive = s.Memo(s => s.D(IsInTree) && s.D(IsEnabled));
        s.Phase(isActive, s => {
            elapsed = 0.0;

            s.OnProcess(delta => {
                elapsed += delta;
                QueueRedraw();
            });

            s.Wait(Duration, () => {
                foreach (var hit in GameState.GroundZone.Query(new Circle(GlobalPosition, World.Px(Radius)))) {
                    var health = hit.Owner.Health;
                    if (health == null) continue;
                    health.Damage(Damage);
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
