using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public enum WaveFront { None, West, East, South, North }

    // Sends enemies in waves: each assault pours in from one edge of the level,
    // and each wave is bigger, faster and heavier than the last, with a lull in
    // between. The next wave's front is chosen when the lull begins, but reads as
    // None until the countdown's last seconds — the UI can't leak an unrevealed direction.
    public class WaveDirector : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject enemy1Prefab;
        [SerializeField] GameObject enemy2Prefab;
        [SerializeField] GameObject enemy3Prefab;

        [Header("Attributes")]
        [SerializeField] float lullDuration = 8f;          // calm between assaults
        [SerializeField] float frontRevealTime = 5f;       // seconds before a wave that its direction is revealed
        [SerializeField] int baseBudget = 4;               // wave 1's spend, in basic-enemy units
        [SerializeField] float budgetPerWave = 2f;         // extra budget each wave (fractions accumulate across waves)
        [SerializeField] int enemy2UnlockWave = 3;         // first wave that can field tier 2
        [SerializeField] int enemy3UnlockWave = 6;         // first wave that can field tier 3
        [SerializeField] float baseSpawnInterval = 1f;     // in-wave spacing at wave 1
        [SerializeField] float spawnIntervalStep = 0.1f;   // spacing shrinks per wave...
        [SerializeField] float minSpawnInterval = 0.25f;   // ...down to this floor
        [SerializeField] float spawnMargin = 2f;           // enemies spawn this far outside the level bounds

        State<int> wave = new();          // 0 until the first assault begins
        State<float> nextWaveIn = new();  // seconds of lull remaining; 0 while assaulting
        State<WaveFront> front = new();   // where the coming (or current) wave attacks from; None until revealed

        public ISignal<int> Wave => wave;
        public ISignal<bool> IsAssaulting => GameState.Assaulting;
        public ISignal<float> NextWaveIn => nextWaveIn;
        public ISignal<WaveFront> Front => front;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);
                s.Phase(isPlaying, s => {
                    var isLull = s.Memo(s => !s.D(GameState.Assaulting));
                    s.Phase(isLull, Lull);
                    s.Phase(GameState.Assaulting, Assault);
                });
            });
        }

        EffectBlock Lull => s => {
            // The front is decided now, but only published in the countdown's last seconds.
            var chosen = (WaveFront)Random.Range(1, 5);
            front.Set(WaveFront.None);
            nextWaveIn.Set(lullDuration);

            IEnumerator onUpdate() {
                var remaining = lullDuration;
                while (remaining > 0f) {
                    yield return null;
                    remaining -= Time.deltaTime;
                    nextWaveIn.Set(Mathf.Max(0f, remaining));
                    if (remaining <= frontRevealTime) front.Set(chosen);
                }
                wave.Update(x => x + 1);
                GameState.Assaulting.Set(true);
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        // The assault ends when every enemy it spawned is dead — each spawn docks a
        // tracker that counts its death exactly once, then undocks (long before the
        // pool can heal or reuse the instance).
        EffectBlock Assault => s => {
            var waveNow = s.D(wave);
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
                    var enemy = Pool.Spawn(prefab, EdgePoint(front.Now), Quaternion.identity).GetComponent<Enemy>();
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
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));

            s.Effect(s => {
                if (s.D(doneSpawning) && s.D(remaining) == 0) GameState.Assaulting.Set(false);
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
