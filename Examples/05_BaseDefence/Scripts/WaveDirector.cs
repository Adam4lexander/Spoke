using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    /// <summary>Which edge of the level a wave attacks from.</summary>
    public enum WaveFront { None, West, East, South, North }

    public readonly struct WaveStatus : System.IEquatable<WaveStatus> {

        /// <summary>The wave number.</summary>
        public readonly int Number;
        /// <summary>The edge this wave attacks from, or None before it's revealed.</summary>
        public readonly WaveFront Front;
        /// <summary>Seconds until the wave begins; 0 once it's attacking.</summary>
        public readonly int StartsIn;

        /// <summary>True while the wave is underway (countdown has reached zero).</summary>
        public bool IsAssaulting => StartsIn == 0;

        public WaveStatus(int number, WaveFront front, int startsIn) {
            Number = number;
            Front = front;
            StartsIn = startsIn;
        }

        public bool Equals(WaveStatus other) =>
            Number == other.Number && Front == other.Front && StartsIn == other.StartsIn;
        public override bool Equals(object obj) => obj is WaveStatus status && Equals(status);
        public override int GetHashCode() => System.HashCode.Combine(Number, Front, StartsIn);
        public static bool operator ==(WaveStatus left, WaveStatus right) => left.Equals(right);
        public static bool operator !=(WaveStatus left, WaveStatus right) => !(left == right);
    }

    // Sends enemies in waves: each assault pours in from one edge of the level, and each wave is
    // bigger, faster and heavier than the last, with a lull in between.
    public class WaveDirector : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject enemy1Prefab;
        [SerializeField] GameObject enemy2Prefab;
        [SerializeField] GameObject enemy3Prefab;

        [Header("Attributes")]
        [SerializeField] float lullDuration = 8f;          // seconds of calm between waves
        [SerializeField] float frontRevealTime = 5f;       // the direction is revealed this many seconds before a wave hits
        [SerializeField] int baseBudget = 4;               // wave 1's spawn budget (a basic enemy costs 1)
        [SerializeField] float budgetPerWave = 2f;         // extra budget added each wave
        [SerializeField] int enemy2UnlockWave = 3;         // first wave with tier 2 enemies
        [SerializeField] int enemy3UnlockWave = 6;         // first wave with tier 3 enemies
        [SerializeField] float baseSpawnInterval = 1f;     // interval between spawns on wave 1
        [SerializeField] float spawnIntervalStep = 0.1f;   // the interval drops this much each wave
        [SerializeField] float minSpawnInterval = 0.25f;   // smallest the interval gets
        [SerializeField] float spawnMargin = 2f;           // enemies spawn this far outside the level bounds

        State<WaveStatus> wave = new();
        Trigger<WaveStatus> waveStarted = Trigger.Create<WaveStatus>();
        Trigger<WaveStatus> waveDefeated = Trigger.Create<WaveStatus>();

        /// <summary>The current wave's status.</summary>
        public ISignal<WaveStatus> Wave => wave;
        /// <summary>Seconds of calm between waves.</summary>
        public float LullDuration => lullDuration;
        /// <summary>Fires when a wave's assault begins.</summary>
        public ITrigger<WaveStatus> WaveStarted => waveStarted;
        /// <summary>Fires when a wave is fully cleared.</summary>
        public ITrigger<WaveStatus> WaveDefeated => waveDefeated;

        protected override void Init(EffectBuilder s) {
            wave.Set(new WaveStatus(1, WaveFront.None, Mathf.CeilToInt(lullDuration)));
            s.Phase(IsEnabled, s => {
                var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);
                s.Phase(isPlaying, s => {
                    var isAssaulting = s.Memo(s => s.D(wave).IsAssaulting);
                    var isLull = s.Memo(s => !s.D(isAssaulting));
                    s.Phase(isLull, Lull);
                    s.Phase(isAssaulting, Assault);
                });
            });
        }

        EffectBlock Lull => s => {
            // The front is decided now, but only published in the countdown's last seconds.
            var chosen = (WaveFront)Random.Range(1, 5);

            IEnumerator onUpdate() {
                var remaining = lullDuration;
                while (remaining > 0f) {
                    var front = remaining <= frontRevealTime ? chosen : WaveFront.None;
                    wave.Set(new WaveStatus(wave.Now.Number, front, Mathf.CeilToInt(remaining)));
                    yield return null;
                    remaining -= Time.deltaTime;
                }
                wave.Set(new WaveStatus(wave.Now.Number, wave.Now.Front, 0));
                waveStarted.Invoke(wave.Now);
            }
            s.Coroutine(onUpdate());
        };

        // The assault ends when every enemy it spawned is dead. Each spawn docks a
        // tracker that counts its death exactly once, then undocks.
        EffectBlock Assault => s => {
            var waveNow = wave.Now.Number;
            var budget = baseBudget + budgetPerWave * (waveNow - 1);
            var interval = Mathf.Max(minSpawnInterval, baseSpawnInterval - spawnIntervalStep * (waveNow - 1));

            var doneSpawning = State.Create(false);
            var remaining = State.Create(0);
            var dock = s.Dock();

            IEnumerator onUpdate() {
                yield return null;
                while (budget > 0) {
                    var (prefab, cost) = PickEnemy(waveNow, budget);
                    budget -= cost;
                    var enemy = Pool.Spawn(prefab, EdgePoint(wave.Now.Front), Quaternion.identity).GetComponent<Enemy>();
                    remaining.Update(x => x + 1);
                    dock.Effect(enemy, s => {
                        if (s.D(enemy.Health.IsAlive)) return;
                        remaining.Update(x => x - 1);
                        dock.Drop(enemy);
                    });
                    yield return new WaitForSeconds(interval);
                }
                doneSpawning.Set(true);
            }
            s.Coroutine(onUpdate());

            s.Effect(s => {
                if (s.D(doneSpawning) && s.D(remaining) == 0) {
                    var defeated = wave.Now;
                    wave.Set(new WaveStatus(defeated.Number + 1, WaveFront.None, Mathf.CeilToInt(lullDuration)));
                    waveDefeated.Invoke(defeated);
                }
            });
        };

        // Tiers cost less than their health multiple: all tiers deal the same damage, so
        // hp concentrated in fewer bodies is worth less than the same hp spread out.
        // Heavier tiers unlock as waves progress, and a pick never overshoots the budget left.
        (GameObject prefab, float cost) PickEnemy(int wave, float budget) {
            var maxTier = wave >= enemy3UnlockWave ? 3 : wave >= enemy2UnlockWave ? 2 : 1;
            if (maxTier > 2 && budget < 2.5f) maxTier = 2;
            if (maxTier > 1 && budget < 1.5f) maxTier = 1;
            return Random.Range(1, maxTier + 1) switch {
                1 => (enemy1Prefab, 1f),
                2 => (enemy2Prefab, 1.5f),
                _ => (enemy3Prefab, 2.5f),
            };
        }

        // A random point along the given front's edge of the level, just outside its bounds.
        Vector3 EdgePoint(WaveFront front) {
            var b = GameState.LevelBounds;
            var x = Random.Range(b.min.x, b.max.x);
            var z = Random.Range(b.min.z, b.max.z);
            return front switch {
                WaveFront.West => new Vector3(b.min.x - spawnMargin, 0f, z),
                WaveFront.East => new Vector3(b.max.x + spawnMargin, 0f, z),
                WaveFront.South => new Vector3(x, 0f, b.min.z - spawnMargin),
                _ => new Vector3(x, 0f, b.max.z + spawnMargin),
            };
        }
    }
}
