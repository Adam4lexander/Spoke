using Godot;

namespace Spoke.Examples.BaseDefence;

// A flying enemy that picks a heading across the base, homes in on a building along it, and bombs it.
public partial class Enemy : SpokeNode {

    [ExportGroup("Prefabs")]
    [Export] public PackedScene BombBlastPrefab { get; set; }

    [ExportGroup("References")]
    [Export] public Health Health { get; set; }
    [Export] public HealthBar HealthBar { get; set; }
    [Export] public UnitFX FX { get; set; }
    [Export] public Node2D FlightRoot { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float Radius { get; set; } = 0.3f;
    [Export] public float MoveSpeed { get; set; } = 2f;
    [Export] public float StopDistance { get; set; } = 1.2f;       // gap kept from the target building's centre
    [Export] public float FireRate { get; set; } = 1f;             // shots per second
    [Export] public float ProximityBias { get; set; } = 0.5f;      // 0 = follow the heading line, higher favours nearby buildings
    [Export] public float SeparationDistance { get; set; } = 1.5f; // enemies closer than this push apart
    [Export] public float SeparationStrength { get; set; } = 2f;   // how hard overlapping enemies repel

    readonly State<bool> tracked = State.Create(false);
    Vector2 flightRootStartPos;

    /// <summary>True while a radar reveals this enemy, letting turrets target it.</summary>
    public ISignal<bool> IsTracked => tracked;

    [Export] public Unit Unit { get; set; }

    protected override void Init(EffectBuilder s) {
        flightRootStartPos = FlightRoot.Position;

        s.Effect(s => {
            var showHealth = s.D(Health.IsAlive) && s.D(Health.HPFraction) < 1f;
            HealthBar.Visible = showHealth;
            HealthBar.Fraction.Set(s.D(Health.HPFraction));
        });

        s.Phase(IsInTree, s => {
            s.Phase(Health.IsAlive, s => {
                s.Use(GameState.EnemyZone.AddCollider(this, () => new Circle(Unit.GlobalPosition, World.Px(Radius))));

                s.Effect(RadarTrack);
                s.Effect(Bob);
                s.Effect(Separate);
                s.Subscribe(Health.Damaged, () => FX.Blink(Palette.DamageFlash));

                var target = s.Effect(ChooseTarget);
                s.Effect(s => {
                    var targetNow = s.D(target);
                    if (targetNow != null) s.Effect(Attack(targetNow));
                });
            });

            var isDead = s.Memo(s => !s.D(Health.IsAlive));
            s.Phase(isDead, s => {
                FX.Shatter();
                s.OnCleanup(FX.Restore);
                s.Effect(s => {
                    if (s.D(FX.IsShattered)) Pool.Despawn(Unit);
                });
            });
        });
    }

    // Each life picks a random heading across the base, and targets the building that
    // best blends "in my path" with "near me", so enemies cut lines through the base
    // instead of all piling onto the nearest building. Once everything lies behind the
    // heading, the score reduces to plain nearest-building.
    EffectBlock<Building> ChooseTarget => s => {
        var target = State.Create<Building>();

        // Aim at the central half of the level, so the line always crosses the base interior.
        var bounds = GameState.LevelBounds;
        var aim = bounds.GetCenter() + 0.5f * new Vector2(
            (float)GD.RandRange(-bounds.Size.X * 0.5, bounds.Size.X * 0.5),
            (float)GD.RandRange(-bounds.Size.Y * 0.5, bounds.Size.Y * 0.5));
        var heading = (aim - Unit.GlobalPosition).Normalized();

        s.OnProcess(_ => {
            Building bestTarget = null;
            var bestScore = float.MaxValue;
            foreach (var building in Building.All) {
                var v = building.Unit.GlobalPosition - Unit.GlobalPosition;
                var along = Mathf.Max(0f, v.Dot(heading));
                var offLine = (v - along * heading).Length();
                var score = offLine + ProximityBias * v.Length();
                if (score < bestScore) {
                    bestScore = score;
                    bestTarget = building;
                }
            }
            target.Set(bestTarget);
        });

        return target;
    };

    EffectBlock Attack(Building target) => s => {
        var inRange = State.Create(false);

        s.OnProcess(delta => {
            if (!GodotObject.IsInstanceValid(target)) return;
            var stop = World.Px(StopDistance);
            var to = target.Unit.GlobalPosition - Unit.GlobalPosition;
            var dist = to.Length();

            // Yaw lives on the flight root, so children of the enemy root (like the
            // health bar) never inherit rotation.
            if (dist > 0.001f) FlightRoot.Rotation = to.Angle();
            if (dist > stop) {
                // The unit moves, not this component. Unity's transform.position is the GameObject's;
                // here the components are children, so moving one would leave the body behind.
                Unit.GlobalPosition += to / dist * Mathf.Min(World.Px(MoveSpeed) * (float)delta, dist - stop);
            }
            inRange.Set(dist <= stop + 1f);
        });

        s.Phase(inRange, s => {
            s.Every(1f / FireRate, () => {
                if (!GodotObject.IsInstanceValid(target)) return;
                Pool.Spawn(BombBlastPrefab, target.Unit.GlobalPosition);
            });
        });
    };

    // A gentle repulsion between living enemies, so they spread out instead of
    // stacking when several converge on the same building.
    EffectBlock Separate => s => {
        var sensor = s.Use(GameState.EnemyZone.AddSensor(
            () => new Circle(Unit.GlobalPosition, World.Px(SeparationDistance))));

        s.OnProcess(delta => {
            var reach = World.Px(SeparationDistance);
            var push = Vector2.Zero;
            foreach (var c in sensor.Overlaps) {
                if (c.Owner == this) continue;
                var away = Unit.GlobalPosition - c.Owner.Unit.GlobalPosition;
                var dist = away.Length();
                if (dist < 0.001f || dist >= reach) continue;
                // Full strength when stacked, fading to zero at the separation distance.
                push += away / dist * (1f - dist / reach);
            }
            Unit.GlobalPosition += World.Px(SeparationStrength) * (float)delta * push;
        });
    };

    EffectBlock RadarTrack => s => {
        var sensor = s.Use(GameState.RadarZone.AddSensor(() => new Circle(Unit.GlobalPosition, World.Px(Radius))));

        var isTracked = s.Memo(s => sensor.Overlaps.Count > 0, sensor.OverlapsChanged);
        s.Phase(isTracked, s => {
            tracked.Set(true);
            s.OnCleanup(() => tracked.Set(false));

            var marker = s.Own(Unit, new DrawLayer(4) {
                OnDraw = l => l.DrawArc(Vector2.Zero, World.Px(Radius) + 8f, 0f, Mathf.Tau, 28,
                                        Palette.TrackedMarker, 2f, true),
            });
            marker.Refresh();
        });
    };

    EffectBlock Bob => s => {
        const float bobAmplitude = 4f;
        const float bobSpeed = 6f;
        var phase = GD.Randf() * Mathf.Pi * 2f;   // desync enemies so they don't bob in lockstep
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            FlightRoot.Position = flightRootStartPos + Vector2.Up * Mathf.Sin((float)elapsed * bobSpeed + phase) * bobAmplitude;
        });
    };
}
