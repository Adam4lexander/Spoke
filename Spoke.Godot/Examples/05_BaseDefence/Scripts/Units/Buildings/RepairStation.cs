using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Heals the most damaged unit inside its coverage, one at a time, while powered.
/// </summary>
public partial class RepairStation : Building {

    /// <summary>Coverage radius in metres.</summary>
    [Export] public float Range { get; set; } = 5f;

    /// <summary>Hit points restored per second.</summary>
    [Export] public float RepairRate { get; set; } = 0.5f;

    protected override string Blurb =>
        "Repairs the most damaged building inside its coverage, one at a time.\n\n" +
        "Repair stations can mend each other, but never themselves.";

    protected override void Alive(EffectBuilder s) {
        base.Alive(s);

        s.Phase(IsRunning(s), s => {
            s.Use(GameState.RepairZone.AddCollider(this, () => new Circle(GlobalPosition, World.Px(Range))));

            var patient = s.Effect(FindPatient);
            s.Effect(s => {
                var patientNow = s.D(patient);
                if (patientNow != null) s.Effect(DoRepair(patientNow));
            });
        });
    }

    // Picks the most damaged unit in range, excluding itself — repair stations can cover each
    // other, but not themselves. Idle while everything nearby is at full health.
    EffectBlock<Unit> FindPatient => s => {
        var patient = State.Create<Unit>();
        var sensor = s.Use(GameState.GroundZone.AddSensor(() => new Circle(GlobalPosition, World.Px(Range))));

        // Let the current patient go once they're healed, or once they've left the sensor —
        // out of range or dead, it's the same departure.
        s.Effect(s => {
            var patientNow = s.D(patient);
            if (patientNow == null) return;
            if (s.D(patientNow.Health.HPFraction) >= 1f) {
                patient.Set(null);
                return;
            }
            foreach (var c in sensor.Overlaps) {
                if (c.Owner == patientNow) return;
            }
            patient.Set(null);
        }, sensor.OverlapsChanged);

        // ...and pick a new one whenever there isn't one.
        s.Effect(s => {
            if (s.D(patient) != null) return;
            Unit best = null;
            var bestFraction = 1f;
            foreach (var c in sensor.Overlaps) {
                if (c.Owner == this) continue;
                var fraction = s.D(c.Owner.Health.HPFraction);
                if (fraction < bestFraction) {
                    bestFraction = fraction;
                    best = c.Owner;
                }
            }
            patient.Set(best);
        }, sensor.OverlapsChanged);

        return patient;
    };

    // Holds the beam on the patient and heals them. Both halves end together, because they are
    // the same block.
    EffectBlock DoRepair(Unit patient) => s => {
        var beam = s.Own(GameState.Board, new DrawLayer(25));
        beam.OnDraw = l => {
            if (!GodotObject.IsInstanceValid(patient)) return;
            l.DrawLine(GlobalPosition, patient.GlobalPosition, Palette.RepairBeam, 2.5f, true);
        };
        s.OnProcess(delta => {
            patient.Health.Repair(RepairRate * (float)delta);
            beam.Refresh();
        });
    };
}
