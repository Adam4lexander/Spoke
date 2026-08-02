using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Reveals enemies inside its coverage so turrets can shoot at them. Does nothing without power.
/// </summary>
public partial class Radar : Building {

    /// <summary>Coverage radius in metres.</summary>
    [Export] public float Range { get; set; } = 8f;

    /// <summary>Dish rotation in degrees per second.</summary>
    [Export] public float DishRotationSpeed { get; set; } = 90f;

    protected override string Blurb =>
        "Reveals enemies inside its coverage to turrets.\n\n" +
        "Turrets cannot fire at an enemy no radar has revealed.";

    protected override void Alive(EffectBuilder s) {
        base.Alive(s);

        s.Phase(IsRunning(s), s => {
            s.Use(GameState.RadarZone.AddCollider(this, () => new Circle(GlobalPosition, World.Px(Range))));

            // The dish only turns while the radar is powered and alive, because that's the only
            // time this block is mounted.
            s.OnProcess(delta => FX.Pivot.Rotation += Mathf.DegToRad(DishRotationSpeed) * (float)delta);
        });
    }
}
