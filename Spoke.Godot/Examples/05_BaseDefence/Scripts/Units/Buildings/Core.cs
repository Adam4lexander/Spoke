using Godot;

namespace Spoke.Examples.BaseDefence;

// The base's root building: the origin of the power grid, and the one whose destruction
// ends the game.
public partial class Core : SpokeNode, IHoverable {

    /// <summary>The unit this component belongs to. Godot's answer to Unity's gameObject.</summary>
    Node2D Unit => Building.Unit;

    [Export] public Building Building { get; set; }
    [Export] public Health Health { get; set; }

    readonly State<HoverInfo> hoverInfo = State.Create(default(HoverInfo));
    public ISignal<HoverInfo> HoverInfo => hoverInfo;

    protected override void Init(EffectBuilder s) {
        hoverInfo.Set(new HoverInfo(
            $"{Building.DisplayName.ToUpper()}\n\n" +
            "Seeds the power grid, all buildings must trace a path to it for receiving power.\n\n" +
            "Game is over if this building is destroyed.",
            CoverageType.Power, Building.Power));

        s.Phase(IsInTree, s => {
            if (s.D(Health.IsAlive)) return;

            // Once the Core dies, play its explosion, cleaned up when the phase unmounts.
            var explode = s.Own(GameState.Board, new DrawLayer(40) { Position = Unit.GlobalPosition });
            var elapsed = 0.0;
            s.OnProcess(delta => {
                elapsed += delta;
                var t = Mathf.Min(1f, (float)(elapsed / 1.6));
                explode.OnDraw = l => l.DrawArc(Vector2.Zero, World.Px(0.6f + 9f * t), 0f, Mathf.Tau, 96,
                                                new Color(Palette.BlastRing, 1f - t), 8f * (1f - t) + 1f, true);
                explode.Refresh();
            });
        });
    }
}
