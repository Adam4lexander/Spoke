using Godot;

namespace Spoke.Examples.BaseDefence;

// Fires at the nearest radar-revealed enemy in its coverage, while powered. Blind without radar.
public partial class Turret : SpokeNode, IHoverable {

    Node2D Unit => Building.Unit;

    [ExportGroup("References")]
    [Export] public Building Building { get; set; }
    [Export] public Node2D Pivot { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float Range { get; set; } = 5f;
    [Export] public float RotationSpeed { get; set; } = 180f;
    [Export] public float Damage { get; set; } = 0.5f;
    [Export] public float FireRate { get; set; } = 2f;       // shots per second
    [Export] public float FireAngle { get; set; } = 2f;      // max muzzle-to-target angle (deg) allowed to fire
    [Export] public float BeamFlashTime { get; set; } = 0.1f;

    float targetDirection;
    readonly State<HoverInfo> hoverInfo = State.Create(default(HoverInfo));

    public ISignal<HoverInfo> HoverInfo => hoverInfo;

    protected override void Init(EffectBuilder s) {
        hoverInfo.Set(new HoverInfo(
            $"{Building.DisplayName.ToUpper()}\n\n" +
            "Fires at enemies inside its coverage that radar has revealed.\n\n" +
            "Pair with radar coverage — a turret alone sees nothing.",
            CoverageType.Turret, Building.Power));

        targetDirection = Pivot.Rotation;

        var isRunning = s.Memo(s => s.D(IsInTree) && s.D(IsEnabled) && s.D(Building.Power.HasPower));

        s.Phase(isRunning, s => {
            s.Effect(RotateToTarget);

            s.Use(GameState.TurretZone.AddCollider(this, () => new Circle(Unit.GlobalPosition, World.Px(Range))));

            // Overlaps are nearest-first, so this picks the closest radar-revealed enemy.
            var sensor = s.Use(GameState.EnemyZone.AddSensor(() => new Circle(Unit.GlobalPosition, World.Px(Range))));
            var target = s.Memo(s => {
                foreach (var c in sensor.Overlaps) {
                    if (s.D(c.Owner.IsTracked)) return c.Owner;
                }
                return null;
            }, sensor.OverlapsChanged);

            s.Effect(s => {
                var targetNow = s.D(target);
                if (targetNow == null) s.Effect(IdleBehaviour);
                else s.Effect(AttackBehaviour(targetNow));
            });
        });
    }

    EffectBlock RotateToTarget => s => {
        s.OnProcess(delta => {
            var step = Mathf.DegToRad(RotationSpeed) * (float)delta;
            var diff = Mathf.AngleDifference(Pivot.Rotation, targetDirection);
            Pivot.Rotation += Mathf.Clamp(diff, -step, step);
        });
    };

    EffectBlock IdleBehaviour => s => {
        const float minInterval = 1f;
        const float maxInterval = 3f;
        var elapsed = 0.0;
        var interval = GD.RandRange(minInterval, maxInterval);
        s.OnProcess(delta => {
            elapsed += delta;
            if (elapsed < interval) return;
            elapsed = 0.0;
            interval = GD.RandRange(minInterval, maxInterval);
            targetDirection = GD.Randf() * Mathf.Tau;
        });
    };

    EffectBlock AttackBehaviour(Enemy target) => s => {
        var ready = State.Create(true);
        var firing = State.Create(false);

        s.OnProcess(_ => {
            if (!GodotObject.IsInstanceValid(target)) return;
            targetDirection = (target.Unit.GlobalPosition - Pivot.GlobalPosition).Angle();
            if (!ready.Now || firing.Now) return;
            if (Mathf.Abs(Mathf.AngleDifference(Pivot.Rotation, targetDirection)) > Mathf.DegToRad(FireAngle)) return;
            firing.Set(true);
            ready.Set(false);
        });

        // Flash the beam first, then land the hit, so the killing shot is seen
        // before the enemy dies and we retarget.
        s.Phase(firing, s => {
            var from = Unit.GlobalPosition;
            var to = GodotObject.IsInstanceValid(target) ? target.Unit.GlobalPosition : from;
            var beam = s.Own(GameState.Board, new DrawLayer(30) {
                OnDraw = l => l.DrawLine(from, to, Palette.TurretBeam, 3f, true),
            });
            beam.Refresh();

            s.Wait(BeamFlashTime, () => {
                if (GodotObject.IsInstanceValid(target)) target.Health.Damage(Damage);
                firing.Set(false);
            });
        });

        var cooling = s.Memo(s => !s.D(ready));
        s.Phase(cooling, s => s.Wait(1f / FireRate, () => ready.Set(true)));
    };
}
