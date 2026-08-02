using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A minable site. While powered, and between waves, it generates money and depletes; once mined
/// out it shatters and stops counting. Clearing every site on the map wins the game.
///
/// Not a Building — it can't be built, and enemies don't target it.
/// </summary>
public partial class ResourceSite : Unit {

    /// <summary>Seconds per unit of money.</summary>
    [Export] public float CollectTime { get; set; } = 0.8f;

    /// <summary>How much this site holds.</summary>
    [Export] public int StartResources { get; set; } = 300;

    readonly State<int> remaining = State.Create(0);

    PowerNode power;

    protected override bool BarTracksHealth => false;

    protected override void Always(EffectBuilder s) {
        power = GetNode<PowerNode>("PowerNode");

        s.Effect(s => {
            var left = s.D(remaining);
            hoverInfo.Set(new HoverInfo(
                left > 0
                    ? $"RESOURCE SITE\n\nGenerates ${1f / CollectTime:0.##}/s while powered. Harvesting pauses during an attack.\n\n{left} remaining."
                    : "RESOURCE SITE\n\nDepleted.",
                CoverageType.None, power));
        });

        s.Effect(s => {
            var fraction = (float)s.D(remaining) / StartResources;
            Bar.Visible = fraction > 0f && fraction < 1f;
            Bar.Fraction.Set(fraction);
        });
    }

    protected override void Alive(EffectBuilder s) {
        remaining.Set(StartResources);

        var hasResources = s.Memo(s => s.D(remaining) > 0);
        var isDepleted = s.Memo(s => !s.D(hasResources));

        // A mined-out site keeps its place in the count until its shatter has finished playing.
        var standing = s.Memo(s => !s.D(FX.IsShattered));
        s.Phase(standing, s => {
            GameState.ResourcesRemaining.Update(x => x + 1);
            s.OnCleanup(() => GameState.ResourcesRemaining.Update(x => x - 1));
        });

        s.Phase(hasResources, s => {
            // Income flows only between waves; an assault pauses every harvester on the map.
            var canHarvest = s.Memo(s => s.D(power.HasPower) && !s.D(GameState.Director.Wave).IsAssaulting);
            s.Phase(canHarvest, Harvest);
        });

        s.Phase(isDepleted, s => {
            FX.Shatter();
            s.OnCleanup(FX.Restore);
        });
    }

    EffectBlock Harvest => s => {
        // The site's contribution to the global collect rate is added on mount and removed on
        // unmount. Nothing anywhere has to recount the harvesters.
        GameState.CollectRate.Update(x => x + 1f / CollectTime);
        s.OnCleanup(() => GameState.CollectRate.Update(x => x - 1f / CollectTime));

        // Unity turns on a particle system here; this is the flat equivalent, and like the
        // particles it exists only while harvesting is actually happening.
        var pulse = s.Own(this, new DrawLayer(3));
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            var t = (float)Mathf.PosMod(elapsed / CollectTime, 1.0);
            pulse.OnDraw = l => l.DrawArc(Vector2.Zero, World.Px(0.5f + 0.35f * t), 0f, Mathf.Tau, 32,
                                          new Color(Palette.Healthy, 0.75f * (1f - t)), 2.5f, true);
            pulse.Refresh();
        });

        s.Every(CollectTime, () => {
            GameState.Money.Update(x => x + 1f);
            remaining.Update(x => x - 1);
        });
    };
}
