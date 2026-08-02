using Godot;

namespace Spoke.Examples.BaseDefence;

// A minable resource site. While powered and between waves it generates money and depletes;
// once mined out it shatters. Clearing every site on the map is the win condition.
//
// Named ResourceSite rather than Unity's Resource, which collides with Godot.Resource.
public partial class ResourceSite : SpokeNode, IHoverable {

    [ExportGroup("References")]
    [Export] public PowerNode Power { get; set; }
    [Export] public HealthBar HealthBar { get; set; }
    [Export] public UnitFX FX { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float Radius { get; set; } = 0.6f;
    [Export] public float CollectTime { get; set; } = 2f;
    [Export] public int StartResources { get; set; } = 20;

    readonly State<int> remaining = State.Create(0);
    readonly State<HoverInfo> hoverInfo = State.Create(default(HoverInfo));

    public ISignal<HoverInfo> HoverInfo => hoverInfo;

    [Export] public Unit Unit { get; set; }

    protected override void Init(EffectBuilder s) {
        s.Phase(IsInTree, s => {
            remaining.Set(StartResources);

            s.Effect(SyncHoverInfo);
            s.Effect(SyncHealthBar);

            s.Use(GameState.GroundZone.AddCollider(Unit, () => new Circle(Unit.GlobalPosition, World.Px(Radius))));

            var hasResources = s.Memo(s => s.D(remaining) > 0);
            var isDepleted = s.Memo(s => !s.D(hasResources));

            // A mined-out site keeps its place in the count until its shatter finishes.
            var standing = s.Memo(s => !s.D(FX.IsShattered));
            s.Phase(standing, s => {
                GameState.ResourcesRemaining.Update(x => x + 1);
                s.OnCleanup(() => GameState.ResourcesRemaining.Update(x => x - 1));
            });

            s.Phase(hasResources, s => {
                // Income flows only between waves; an assault pauses every harvester.
                var canHarvest = s.Memo(s => s.D(Power.HasPower) && !s.D(GameState.Director.Wave).IsAssaulting);
                s.Phase(canHarvest, Harvest);
            });

            s.Phase(isDepleted, s => {
                FX.Shatter();
                s.OnCleanup(FX.Restore);
            });
        });
    }

    EffectBlock SyncHoverInfo => s => {
        var left = s.D(remaining);
        var description = left > 0
            ? $"RESOURCE\n\nGenerates ${1f / CollectTime:0.##}/s while powered. Harvesting pauses during an attack.\n\n{left} remaining."
            : "RESOURCE\n\nDepleted.";
        hoverInfo.Set(new HoverInfo(description, CoverageType.None, Power));
    };

    EffectBlock SyncHealthBar => s => {
        var frac = (float)s.D(remaining) / StartResources;
        HealthBar.Visible = frac < 1f && frac > 0f;
        HealthBar.Fraction.Set(frac);
    };

    EffectBlock Harvest => s => {
        // Unity turns on a particle system here; this is the flat equivalent.
        var harvestFX = s.Own(Unit, new DrawLayer(3));
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            var t = (float)Mathf.PosMod(elapsed / CollectTime, 1.0);
            harvestFX.OnDraw = l => l.DrawArc(Vector2.Zero, World.Px(0.5f + 0.35f * t), 0f, Mathf.Tau, 32,
                                              new Color(Palette.Healthy, 0.75f * (1f - t)), 2.5f, true);
            harvestFX.Refresh();
        });

        GameState.CollectRate.Update(x => x + 1f / CollectTime);
        s.OnCleanup(() => GameState.CollectRate.Update(x => x - 1f / CollectTime));

        s.Every(CollectTime, () => {
            GameState.Money.Update(x => x + 1);
            remaining.Update(x => x - 1);
        });
    };
}
