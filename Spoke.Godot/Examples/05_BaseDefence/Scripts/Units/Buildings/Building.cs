using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A player-owned structure: something with a price, a place on the grid, and a power link.
/// Everything shared by the five buildings lives here.
///
/// Name, price and coverage kind are exported, so the unit's scene is the single place they're
/// written down — the same role the prefab plays in the Unity version.
/// </summary>
public abstract partial class Building : Unit {

    static readonly List<Building> all = new();

    /// <summary>Every building currently standing. Enemies pick their targets from it.</summary>
    public static ReadOnlyList<Building> All => new(all);

    [Export] public string DisplayName { get; set; } = "Building";
    [Export] public int Cost { get; set; }

    /// <summary>Which overlay to draw while this building is hovered or being placed.</summary>
    [Export] public CoverageType Coverage { get; set; } = CoverageType.Power;

    /// <summary>What this building says when hovered, below its name.</summary>
    protected abstract string Blurb { get; }

    public PowerNode Power { get; private set; }

    protected override void Always(EffectBuilder s) {
        Power = GetNode<PowerNode>("PowerNode");

        hoverInfo.Set(new HoverInfo($"{DisplayName.ToUpper()}\n\n{Blurb}", Coverage, Power));

        // Unpowered buildings read as dim. This is the whole of "show the player what's offline".
        s.Effect(s => FX.SetTint(s.D(Power.HasPower) ? Colors.White : Palette.Unpowered));
    }

    protected override void Alive(EffectBuilder s) {
        all.Add(this);
        s.OnCleanup(() => all.Remove(this));
    }

    /// <summary>True while this building is powered — what every building's own work is gated on.</summary>
    protected ISignal<bool> IsRunning(EffectBuilder s)
        => s.Memo(s => s.D(Power.HasPower));
}
