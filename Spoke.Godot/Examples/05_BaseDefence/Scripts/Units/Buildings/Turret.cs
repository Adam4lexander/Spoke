using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Fires at the nearest radar-revealed enemy inside its coverage, while powered. Blind on its own.
/// </summary>
public partial class Turret : Building {

    /// <summary>Coverage radius in metres.</summary>
    [Export] public float Range { get; set; } = 5f;

    /// <summary>Barrel traverse in degrees per second.</summary>
    [Export] public float RotationSpeed { get; set; } = 180f;

    /// <summary>Damage per shot, in hit points.</summary>
    [Export] public float Damage { get; set; } = 0.5f;

    /// <summary>Shots per second.</summary>
    [Export] public float FireRate { get; set; } = 2f;

    /// <summary>How closely the barrel must line up before it fires, in degrees.</summary>
    [Export] public float FireAngle { get; set; } = 2f;

    /// <summary>How long the beam stays on screen, in seconds.</summary>
    [Export] public float BeamFlashTime { get; set; } = 0.1f;

    protected override string Blurb =>
        "Fires at enemies inside its coverage that radar has revealed.\n\n" +
        "Pair it with radar coverage — a turret alone sees nothing.";

    protected override void Alive(EffectBuilder s) {
        base.Alive(s);

        s.Phase(IsRunning(s), s => {
            var rangePx = World.Px(Range);
            s.Use(GameState.TurretZone.AddCollider(this, () => new Circle(GlobalPosition, rangePx)));

            // Overlaps arrive nearest-first, so this is the closest enemy radar has revealed.
            var sensor = s.Use(GameState.EnemyZone.AddSensor(() => new Circle(GlobalPosition, rangePx)));
            var target = s.Memo(s => {
                foreach (var c in sensor.Overlaps) {
                    if (s.D(c.Owner.IsTracked)) return c.Owner;
                }
                return null;
            }, sensor.OverlapsChanged);

            // Where the barrel wants to point. Whichever behaviour is mounted writes it; the
            // rotation below reads it, and neither has to know about the other.
            var aim = FX.Pivot.Rotation;

            s.OnProcess(delta => {
                var step = Mathf.DegToRad(RotationSpeed) * (float)delta;
                var diff = Mathf.AngleDifference(FX.Pivot.Rotation, aim);
                FX.Pivot.Rotation += Mathf.Clamp(diff, -step, step);
            });

            // Nothing to shoot: sweep to a new bearing every couple of seconds.
            EffectBlock idle = s => {
                aim = GD.Randf() * Mathf.Tau;
                s.Every(GD.RandRange(1.0, 3.0), () => aim = GD.Randf() * Mathf.Tau);
            };

            // Track the target, and fire when the cooldown is up and the barrel is lined up.
            EffectBlock attack(Enemy enemy) => s => {
                var ready = State.Create(true);
                var firing = State.Create(false);

                s.OnProcess(_ => {
                    if (!GodotObject.IsInstanceValid(enemy)) return;
                    aim = (enemy.GlobalPosition - GlobalPosition).Angle();
                    if (!ready.Now || firing.Now) return;
                    if (Mathf.Abs(Mathf.AngleDifference(FX.Pivot.Rotation, aim)) > Mathf.DegToRad(FireAngle)) return;
                    firing.Set(true);
                });

                // The beam is a phase, so it's on screen for exactly as long as the shot lasts —
                // and if the turret loses power mid-shot, the beam goes with everything else.
                s.Phase(firing, s => {
                    var from = GlobalPosition;
                    var to = GodotObject.IsInstanceValid(enemy) ? enemy.GlobalPosition : from;
                    var beam = s.Own(GameState.Board, new DrawLayer(30) {
                        OnDraw = l => l.DrawLine(from, to, Palette.TurretBeam, 3f, true),
                    });
                    beam.Refresh();

                    // Show the beam first, then land the hit, so the killing shot is seen before
                    // the enemy dies and the turret retargets.
                    s.Wait(BeamFlashTime, () => {
                        if (GodotObject.IsInstanceValid(enemy)) enemy.Health.Damage(Damage);
                        firing.Set(false);
                        ready.Set(false);
                    });
                });

                var cooling = s.Memo(s => !s.D(ready));
                s.Phase(cooling, s => s.Wait(1f / FireRate, () => ready.Set(true)));
            };

            s.Effect(s => {
                var targetNow = s.D(target);
                if (targetNow == null) s.Effect("Idle", idle);
                else s.Effect("Attack", attack(targetNow));
            });
        });
    }
}
