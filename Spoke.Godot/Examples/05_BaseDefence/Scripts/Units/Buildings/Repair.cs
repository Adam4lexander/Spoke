using Godot;

namespace Spoke.Examples.BaseDefence;

// Heals the most damaged building in its coverage, one at a time, while powered.
public partial class Repair : SpokeNode, IHoverable {

    /// <summary>The unit this component belongs to. Godot's answer to Unity's gameObject.</summary>
    Node2D Unit => Building.Unit;

    [ExportGroup("References")]
    [Export] public Building Building { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float Range { get; set; } = 5f;
    [Export] public float RepairRate { get; set; } = 0.5f;   // HP per second

    readonly State<HoverInfo> hoverInfo = State.Create(default(HoverInfo));
    public ISignal<HoverInfo> HoverInfo => hoverInfo;

    protected override void Init(EffectBuilder s) {
        hoverInfo.Set(new HoverInfo(
            $"{Building.DisplayName.ToUpper()}\n\n" +
            "Repairs the most damaged building inside its coverage, one at a time.\n\n" +
            "Repair buildings can mend each other, but never themselves.",
            CoverageType.Repair, Building.Power));

        var isRunning = s.Memo(s => s.D(IsInTree) && s.D(Building.Power.HasPower));

        s.Phase(isRunning, s => {
            s.Use(GameState.RepairZone.AddCollider(this, () => new Circle(Unit.GlobalPosition, World.Px(Range))));

            var patient = s.Effect(FindPatient);
            s.Effect(s => {
                var patientNow = s.D(patient);
                if (patientNow != null) s.Effect(DoRepair(patientNow));
            });
        });
    }

    // Takes the most damaged building in range (excluding its own; repair towers can
    // cover each other, but not themselves). Idle while everyone's at full health.
    EffectBlock<Health> FindPatient => s => {
        var patient = State.Create<Health>();
        var sensor = s.Use(GameState.GroundZone.AddSensor(() => new Circle(Unit.GlobalPosition, World.Px(Range))));

        s.Effect(s => {
            var patientNow = s.D(patient);
            if (patientNow == null) return;
            if (s.D(patientNow.HPFraction) >= 1f) {
                patient.Set(null);
                return;
            }
            foreach (var c in sensor.Overlaps) {
                if (c.Owner == patientNow.Owner) return;
            }
            patient.Set(null);
        }, sensor.OverlapsChanged);

        s.Effect(s => {
            if (s.D(patient) != null) return;
            Health best = null;
            var bestFrac = 1f;
            foreach (var c in sensor.Overlaps) {
                if (c.Owner == Building.Unit) continue;
                var health = c.Owner.GetNodeOrNull<Health>("Health");
                if (health == null) continue;
                var frac = s.D(health.HPFraction);
                if (frac < bestFrac) {
                    bestFrac = frac;
                    best = health;
                }
            }
            patient.Set(best);
        }, sensor.OverlapsChanged);

        return patient;
    };

    // Heals the patient with the beam held on them, releasing them once they're healed
    // or gone (out of range or dead; either way their collider has left the sensor).
    EffectBlock DoRepair(Health patient) => s => {
        var beam = s.Own(GameState.Board, new DrawLayer(25));
        beam.OnDraw = l => {
            if (!GodotObject.IsInstanceValid(patient)) return;
            l.DrawLine(Unit.GlobalPosition, patient.Unit.GlobalPosition, Palette.RepairBeam, 2.5f, true);
        };
        s.OnProcess(delta => {
            patient.Repair(RepairRate * (float)delta);
            beam.Refresh();
        });
    };
}
