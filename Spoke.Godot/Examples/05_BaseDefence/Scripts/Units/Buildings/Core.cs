using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The base's root building: the origin of the power grid, and the one whose destruction ends the
/// game. It never has a parent — it is the parent every other node's chain has to reach.
/// </summary>
public partial class Core : Building {

    static readonly State<bool> isStanding = State.Create(false);

    /// <summary>True while the Core is on the board and alive. GameState watches it for the loss condition.</summary>
    public static ISignal<bool> IsStanding => isStanding;

    protected override string Blurb =>
        "Seeds the power grid. Every building must trace a path back to it to receive power.\n\n" +
        "The game is over if this building is destroyed.";

    protected override void Always(EffectBuilder s) {
        base.Always(s);

        // Standing means "on the board", not "alive" — the Core holds its place through its death
        // shatter and only then goes back to the pool. Gating the loss on IsInTree rather than on
        // health is what lets the explosion play before the game freezes.
        s.Phase(IsInTree, s => {
            isStanding.Set(true);
            s.OnCleanup(() => isStanding.Set(false));
        });
    }

    protected override void Dying(EffectBuilder s) {
        // A last flare where the Core stood, cleaned up when the phase unmounts. Unity spawns a
        // particle prefab here and despawns it the same way.
        var blast = s.Own(GameState.Board, new DrawLayer(40) { Position = GlobalPosition });
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            var t = Mathf.Min(1f, (float)(elapsed / 1.6));
            blast.OnDraw = l => l.DrawArc(Vector2.Zero, World.Px(0.6f + 9f * t), 0f, Mathf.Tau, 96,
                                          new Color(Palette.BlastRing, 1f - t), 8f * (1f - t) + 1f, true);
            blast.Refresh();
        });
    }
}
