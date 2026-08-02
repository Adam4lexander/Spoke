using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The player's contact with the board: hovering, and placing. Every overlay the two need is a
/// nested block, so what's on screen is always exactly what the current situation calls for —
/// there is no show/hide bookkeeping anywhere in this file.
/// </summary>
public partial class BoardInteractions : SpokeNode {

    readonly State<IHoverable> hovering = State.Create<IHoverable>(null);
    readonly State<Circle> hoveringCircle = State.Create(default(Circle));

    /// <summary>The build item currently being placed, or null when not placing.</summary>
    public State<BuildItem> Placing { get; } = State.Create<BuildItem>(null);

    /// <summary>The unit under the pointer, or null when there isn't one.</summary>
    public ISignal<IHoverable> Hovering => hovering;

    protected override void Init(EffectBuilder s) {
        var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);

        s.Phase(isPlaying, s => {
            s.OnCleanup(() => Placing.Set(null));

            var hasMousePoint = s.Memo(s => s.D(GameState.View).MousePoint != null);

            // The grid's whole spanning tree, shown unless a single unit's chain is in focus or a
            // placement is under way.
            s.Effect(s => {
                if (s.D(hovering) != null || s.D(Placing) != null) return;
                s.Effect(LinkDisplay.DrawAll(new Color(Palette.PowerLink, 0.45f)));
            });

            s.Effect(ShowHovered);

            s.Phase(hasMousePoint, s => {
                var placingNow = s.D(Placing);
                if (placingNow == null) s.Effect(FindHovered);
                else s.Effect(PlaceBuilding(placingNow));
            });
        });
    }

    // Publishes the unit under the pointer. A zero-radius sensor is a point query that stays live.
    EffectBlock FindHovered => s => {
        var sensor = s.Use(GameState.GroundZone.AddSensor(
            () => new Circle(GameState.View.Now.MousePoint ?? Vector2.Zero, 0f)));

        s.OnCleanup(() => {
            hovering.Set(null);
            hoveringCircle.Set(default);
        });

        s.Effect(s => {
            var overlap = sensor.Overlaps.Count > 0 ? sensor.Overlaps[0] : null;
            hovering.Set(overlap?.Owner.Describes as IHoverable);
            hoveringCircle.Set(overlap?.Circle ?? default);
        }, sensor.OverlapsChanged);
    };

    EffectBlock ShowHovered => s => {
        var hoverable = s.D(hovering);
        if (hoverable == null) return;

        var coverage = s.Memo(s => s.D(hoverable.HoverInfo).Coverage);
        s.Effect(s => s.Effect(ShowCoverage(s.D(coverage))));

        var powerNode = s.Memo(s => s.D(hoverable.HoverInfo).PowerNode);
        s.Effect(s => {
            var node = s.D(powerNode);
            if (node != null) s.Effect(LinkDisplay.Draw(node, Palette.PowerLink));
        });

        // A ring a little larger than the unit's own footprint.
        var circle = s.D(hoveringCircle);
        s.Effect(CoverageDisplay.Draw(new Circle(circle.Center, circle.Radius * 1.4f), Palette.HoverRing));
    };

    // Power coverage and the placed type's own coverage show while choosing a spot, and the
    // footprint follows the pointer, recoloured by whether it can go there — touching provider
    // coverage, clear of everything else. A click on a valid spot buys it.
    EffectBlock PlaceBuilding(BuildItem item) => s => {
        s.Effect(CoverageDisplay.Draw(GameState.PowerZone, Palette.PowerCoverage, body => body.IsProvider));
        if (item.Coverage != CoverageType.Power) s.Effect(ShowCoverage(item.Coverage));

        var mouse = s.Memo(s => s.D(GameState.View).MousePoint ?? Vector2.Zero);
        var footprint = s.Memo(s => new Circle(s.D(mouse), World.Px(item.Radius)));

        var groundSensor = s.Use(GameState.GroundZone.AddSensor(() => footprint.Now));
        var powerSensor = s.Use(GameState.PowerZone.AddSensor(() => new Circle(mouse.Now, 0f), body => body.IsProvider));

        var isValid = s.Memo(
            s => groundSensor.Overlaps.Count == 0 && powerSensor.Overlaps.Count > 0,
            groundSensor.OverlapsChanged, powerSensor.OverlapsChanged);

        var colour = s.Memo(s => s.D(isValid) ? Palette.ValidPlacement : Palette.InvalidPlacement);
        s.Effect(CoverageDisplay.Draw(footprint, colour));

        s.Subscribe(InputSignals.LeftClick, () => {
            if (!isValid.Now) return;
            Pool.Spawn(item.Prefab, mouse.Now);
            GameState.Money.Update(x => x - item.Cost);
            Placing.Set(null);
        });

        s.Subscribe(InputSignals.RightClick, () => Placing.Set(null));
        s.Subscribe(InputSignals.KeyDown(Key.Escape), () => Placing.Set(null));
    };

    // One coverage type, one zone, one colour.
    EffectBlock ShowCoverage(CoverageType type) => s => {
        switch (type) {
            case CoverageType.Power:
                s.Effect(CoverageDisplay.Draw(GameState.PowerZone, Palette.PowerCoverage, body => body.IsProvider));
                break;
            case CoverageType.Radar:
                s.Effect(CoverageDisplay.Draw(GameState.RadarZone, Palette.RadarCoverage));
                break;
            case CoverageType.Turret:
                s.Effect(CoverageDisplay.Draw(GameState.TurretZone, Palette.TurretCoverage));
                break;
            case CoverageType.Repair:
                s.Effect(CoverageDisplay.Draw(GameState.RepairZone, Palette.RepairCoverage));
                break;
        }
    };
}
