using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A flyer that picks a heading across the base, homes in on a building along it, and bombs it.
/// All three tiers share this script; they differ only in the exported values in their scenes.
/// </summary>
public partial class Enemy : Unit {

    /// <summary>Metres per second.</summary>
    [Export] public float MoveSpeed { get; set; } = 1f;

    /// <summary>Gap kept from the target building's centre, in metres.</summary>
    [Export] public float StopDistance { get; set; } = 1.2f;

    /// <summary>Bombs dropped per second.</summary>
    [Export] public float FireRate { get; set; } = 1f;

    /// <summary>0 follows the heading line exactly; higher favours nearby buildings.</summary>
    [Export] public float ProximityBias { get; set; } = 0.5f;

    /// <summary>Enemies closer than this push apart, in metres.</summary>
    [Export] public float SeparationDistance { get; set; } = 0.6f;

    /// <summary>How hard overlapping enemies repel, in metres per second.</summary>
    [Export] public float SeparationStrength { get; set; } = 0.2f;

    readonly State<bool> tracked = State.Create(false);

    /// <summary>True while a radar reveals this enemy, letting turrets target it.</summary>
    public ISignal<bool> IsTracked => tracked;

    protected override bool OccupiesGround => false;

    protected override void Alive(EffectBuilder s) {
        s.Use(GameState.EnemyZone.AddCollider(this, () => new Circle(GlobalPosition, RadiusPx)));

        s.Effect(RadarTrack);
        s.Effect(Separate);

        var target = s.Effect(ChooseTarget);
        s.Effect(s => {
            var targetNow = s.D(target);
            if (targetNow != null) s.Effect(Attack(targetNow));
        });
    }

    // Each life picks a random heading across the base and targets the building that best blends
    // "in my path" with "near me", so enemies cut lines through the base instead of all piling onto
    // the nearest building. Once everything lies behind the heading, the score reduces to nearest.
    EffectBlock<Building> ChooseTarget => s => {
        var target = State.Create<Building>();

        // Aim at the central half of the level, so the line always crosses the base interior.
        var bounds = GameState.LevelBounds;
        var aim = bounds.GetCenter() + 0.5f * new Vector2(
            (float)GD.RandRange(-bounds.Size.X * 0.5, bounds.Size.X * 0.5),
            (float)GD.RandRange(-bounds.Size.Y * 0.5, bounds.Size.Y * 0.5));
        var heading = (aim - GlobalPosition).Normalized();

        s.OnProcess(_ => {
            Building best = null;
            var bestScore = float.MaxValue;
            foreach (var building in Building.All) {
                var v = building.GlobalPosition - GlobalPosition;
                var along = Mathf.Max(0f, v.Dot(heading));
                var offLine = (v - along * heading).Length();
                var score = offLine + ProximityBias * v.Length();
                if (score >= bestScore) continue;
                bestScore = score;
                best = building;
            }
            target.Set(best);
        });

        return target;
    };

    EffectBlock Attack(Building target) => s => {
        var inRange = State.Create(false);

        s.OnProcess(delta => {
            if (!GodotObject.IsInstanceValid(target)) return;
            var stop = World.Px(StopDistance);
            var to = target.GlobalPosition - GlobalPosition;
            var distance = to.Length();
            if (distance > 0.001f) FX.Rotation = to.Angle();
            if (distance > stop) {
                GlobalPosition += to / distance * Mathf.Min(World.Px(MoveSpeed) * (float)delta, distance - stop);
            }
            inRange.Set(distance <= stop + 1f);
        });

        // Bombing is a phase over "am I close enough". Drift out of range and the bombing stops;
        // drift back and it restarts, with no cooldown bookkeeping either way.
        s.Phase(inRange, s => {
            s.Every(1f / FireRate, () => {
                if (!GodotObject.IsInstanceValid(target)) return;
                Pool.Spawn(Units.BombBlast, target.GlobalPosition);
            });
        });
    };

    // Gentle repulsion between living enemies, so they spread out instead of stacking when several
    // converge on the same building.
    EffectBlock Separate => s => {
        var sensor = s.Use(GameState.EnemyZone.AddSensor(
            () => new Circle(GlobalPosition, World.Px(SeparationDistance))));

        s.OnProcess(delta => {
            var reach = World.Px(SeparationDistance);
            var push = Vector2.Zero;
            foreach (var c in sensor.Overlaps) {
                if (c.Owner == this) continue;
                var away = GlobalPosition - c.Owner.GlobalPosition;
                var distance = away.Length();
                if (distance < 0.001f || distance >= reach) continue;
                // Full strength when stacked, fading to zero at the separation distance.
                push += away / distance * (1f - distance / reach);
            }
            GlobalPosition += World.Px(SeparationStrength) * (float)delta * push;
        });
    };

    EffectBlock RadarTrack => s => {
        var sensor = s.Use(GameState.RadarZone.AddSensor(() => new Circle(GlobalPosition, RadiusPx)));
        var isTracked = s.Memo(s => sensor.Overlaps.Count > 0, sensor.OverlapsChanged);

        s.Phase(isTracked, s => {
            tracked.Set(true);
            s.OnCleanup(() => tracked.Set(false));

            // The marker exists for exactly as long as the tracking does. Unity toggles a child
            // object; here the child is created by the phase, which can't fall out of step.
            var marker = s.Own(this, new DrawLayer(4) {
                OnDraw = l => l.DrawArc(Vector2.Zero, RadiusPx + 8f, 0f, Mathf.Tau, 28,
                                        Palette.TrackedMarker, 2f, true),
            });
            marker.Refresh();
        });
    };
}
