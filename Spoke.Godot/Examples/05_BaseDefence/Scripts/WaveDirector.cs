using System;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>Which edge of the level a wave attacks from.</summary>
public enum WaveFront { None, West, East, North, South }

public readonly struct WaveStatus : IEquatable<WaveStatus> {

    /// <summary>The wave number.</summary>
    public readonly int Number;
    /// <summary>The edge this wave attacks from, or None before it's revealed.</summary>
    public readonly WaveFront Front;
    /// <summary>Seconds until the wave begins; 0 once it's attacking.</summary>
    public readonly int StartsIn;

    /// <summary>True while the wave is underway.</summary>
    public bool IsAssaulting => StartsIn == 0;

    public WaveStatus(int number, WaveFront front, int startsIn) {
        Number = number;
        Front = front;
        StartsIn = startsIn;
    }

    public bool Equals(WaveStatus other)
        => Number == other.Number && Front == other.Front && StartsIn == other.StartsIn;

    public override bool Equals(object obj) => obj is WaveStatus status && Equals(status);
    public override int GetHashCode() => HashCode.Combine(Number, Front, StartsIn);
    public static bool operator ==(WaveStatus a, WaveStatus b) => a.Equals(b);
    public static bool operator !=(WaveStatus a, WaveStatus b) => !a.Equals(b);
}

/// <summary>
/// Sends enemies in waves. Each assault pours in from one edge, and each wave is bigger, faster and
/// heavier than the last, with a lull in between.
/// </summary>
public partial class WaveDirector : SpokeNode {

    [ExportGroup("Prefabs")]
    [Export] public PackedScene Enemy1Prefab { get; set; }
    [Export] public PackedScene Enemy2Prefab { get; set; }
    [Export] public PackedScene Enemy3Prefab { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float LullDuration { get; set; } = 30f;          // seconds of calm between waves
    [Export] public float FrontRevealTime { get; set; } = 5f;        // the direction is revealed this many seconds before a wave hits
    [Export] public int BaseBudget { get; set; } = 2;                // wave 1's spawn budget (a basic enemy costs 1)
    [Export] public float BudgetPerWave { get; set; } = 1f;          // extra budget added each wave
    [Export] public int Enemy2UnlockWave { get; set; } = 4;          // first wave with tier 2 enemies
    [Export] public int Enemy3UnlockWave { get; set; } = 8;          // first wave with tier 3 enemies
    [Export] public float BaseSpawnInterval { get; set; } = 1f;      // interval between spawns on wave 1
    [Export] public float SpawnIntervalStep { get; set; } = 0.1f;    // the interval drops this much each wave
    [Export] public float MinSpawnInterval { get; set; } = 0.25f;    // smallest the interval gets
    [Export] public float SpawnMargin { get; set; } = 6f;            // enemies spawn this far outside the level bounds

    readonly State<WaveStatus> wave = State.Create(default(WaveStatus));
    readonly Trigger<WaveStatus> waveStarted = Trigger.Create<WaveStatus>();
    readonly Trigger<WaveStatus> waveDefeated = Trigger.Create<WaveStatus>();

    /// <summary>The current wave's status.</summary>
    public ISignal<WaveStatus> Wave => wave;
    /// <summary>Fires when a wave's assault begins.</summary>
    public ITrigger<WaveStatus> WaveStarted => waveStarted;
    /// <summary>Fires when a wave is fully cleared.</summary>
    public ITrigger<WaveStatus> WaveDefeated => waveDefeated;

    protected override void Init(EffectBuilder s) {
        wave.Set(new WaveStatus(1, WaveFront.None, Mathf.CeilToInt(LullDuration)));

        var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);

        s.Phase(isPlaying, s => {
            var isAssaulting = s.Memo(s => s.D(wave).IsAssaulting);
            var isLull = s.Memo(s => !s.D(isAssaulting));
            s.Phase(isLull, Lull);
            s.Phase(isAssaulting, Assault);
        });
    }

    EffectBlock Lull => s => {
        // The front is decided now, but only published in the countdown's last seconds.
        var chosen = (WaveFront)GD.RandRange(1, 4);
        var remaining = (double)LullDuration;

        s.OnProcess(delta => {
            remaining -= delta;
            if (remaining > 0.0) {
                var front = remaining <= FrontRevealTime ? chosen : WaveFront.None;
                wave.Set(new WaveStatus(wave.Now.Number, front, Mathf.CeilToInt(remaining)));
                return;
            }
            // StartsIn hits zero, IsAssaulting flips, and this whole phase swaps for Assault.
            wave.Set(new WaveStatus(wave.Now.Number, chosen, 0));
            waveStarted.Invoke(wave.Now);
        });
    };

    // The assault ends when every enemy it spawned is dead. Each spawn docks a tracker that counts
    // its death exactly once, then undocks itself.
    EffectBlock Assault => s => {
        var number = wave.Now.Number;
        var front = wave.Now.Front;
        var budget = BaseBudget + BudgetPerWave * (number - 1);
        var interval = Mathf.Max(MinSpawnInterval, BaseSpawnInterval - SpawnIntervalStep * (number - 1));

        var doneSpawning = State.Create(false);
        var remaining = State.Create(0);
        var dock = s.Dock();

        s.Every(interval, () => {
            if (budget <= 0f) {
                doneSpawning.Set(true);
                return;
            }
            var (prefab, cost) = PickEnemy(number, budget);
            budget -= cost;

            var enemy = Pool.Spawn(prefab, EdgePoint(front)).GetNode<Enemy>("Enemy");
            remaining.Update(x => x + 1);
            dock.Effect(enemy, s => {
                if (s.D(enemy.Health.IsAlive)) return;
                remaining.Update(x => x - 1);
                dock.Drop(enemy);
            });
        });

        s.Effect(s => {
            if (!s.D(doneSpawning) || s.D(remaining) != 0) return;
            var defeated = wave.Now;
            wave.Set(new WaveStatus(defeated.Number + 1, WaveFront.None, Mathf.CeilToInt(LullDuration)));
            waveDefeated.Invoke(defeated);
        });
    };

    // Tiers cost less than their health multiple: every tier deals the same damage, so hp
    // concentrated in fewer bodies is worth less than the same hp spread out. Heavier tiers unlock
    // as waves progress, and a pick never overshoots the budget left.
    (PackedScene prefab, float cost) PickEnemy(int wave, float budget) {
        var maxTier = wave >= Enemy3UnlockWave ? 3 : wave >= Enemy2UnlockWave ? 2 : 1;
        if (maxTier > 2 && budget < 2.5f) maxTier = 2;
        if (maxTier > 1 && budget < 1.5f) maxTier = 1;
        return GD.RandRange(1, maxTier) switch {
            1 => (Enemy1Prefab, 1f),
            2 => (Enemy2Prefab, 1.5f),
            _ => (Enemy3Prefab, 2.5f),
        };
    }

    // A random point along the given front's edge of the level, just outside its bounds.
    Vector2 EdgePoint(WaveFront front) {
        var b = GameState.LevelBounds;
        var margin = World.Px(SpawnMargin);
        var x = (float)GD.RandRange(b.Position.X, b.End.X);
        var y = (float)GD.RandRange(b.Position.Y, b.End.Y);
        return front switch {
            WaveFront.West => new Vector2(b.Position.X - margin, y),
            WaveFront.East => new Vector2(b.End.X + margin, y),
            WaveFront.North => new Vector2(x, b.Position.Y - margin),
            _ => new Vector2(x, b.End.Y + margin),
        };
    }
}
