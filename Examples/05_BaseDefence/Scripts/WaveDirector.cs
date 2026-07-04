using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public enum Edge { West, East, South, North }

    // Sends enemies in waves: each assault pours in from one edge of the level,
    // and each wave is bigger and faster than the last, with a lull in between.
    // The next wave's front is chosen when the lull begins, so the UI can warn
    // where the attack will come from while the countdown runs.
    public class WaveDirector : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject enemyPrefab;

        [Header("Attributes")]
        [SerializeField] float lullDuration = 8f;          // calm between assaults
        [SerializeField] int baseCount = 4;                // enemies in wave 1
        [SerializeField] int countPerWave = 2;             // extra enemies each wave
        [SerializeField] float baseSpawnInterval = 1f;     // in-wave spacing at wave 1
        [SerializeField] float spawnIntervalStep = 0.1f;   // spacing shrinks per wave...
        [SerializeField] float minSpawnInterval = 0.25f;   // ...down to this floor

        State<int> wave = new();          // 0 until the first assault begins
        State<bool> assaulting = new();
        State<float> nextWaveIn = new();  // seconds of lull remaining; 0 while assaulting
        State<Edge> front = new();        // where the coming (or current) wave attacks from

        public ISignal<int> Wave => wave;
        public ISignal<bool> IsAssaulting => assaulting;
        public ISignal<float> NextWaveIn => nextWaveIn;
        public ISignal<Edge> Front => front;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);
                s.Phase(isPlaying, s => {
                    var isLull = s.Memo(s => !s.D(assaulting));
                    s.Phase(isLull, Lull);
                    s.Phase(assaulting, Assault);
                });
            });
        }

        EffectBlock Lull => s => {
            front.Set((Edge)Random.Range(0, 4));
            nextWaveIn.Set(lullDuration);

            IEnumerator onUpdate() {
                var remaining = lullDuration;
                while (remaining > 0f) {
                    yield return null;
                    remaining -= Time.deltaTime;
                    nextWaveIn.Set(Mathf.Max(0f, remaining));
                }
                wave.Update(x => x + 1);
                assaulting.Set(true);
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        // The assault ends when every enemy it spawned is dead — each spawn docks a
        // tracker that counts its death exactly once, then undocks (long before the
        // pool can heal or reuse the instance).
        EffectBlock Assault => s => {
            var waveNow = s.D(wave);
            var count = baseCount + countPerWave * (waveNow - 1);
            var interval = Mathf.Max(minSpawnInterval, baseSpawnInterval - spawnIntervalStep * (waveNow - 1));

            var doneSpawning = State.Create(false);
            var remaining = State.Create(0);
            var dock = s.Dock();

            IEnumerator onUpdate() {
                yield return null;
                for (int i = 0; i < count; i++) {
                    var enemy = Pool.Spawn(enemyPrefab, EdgePoint(front.Now), Quaternion.identity).GetComponent<Enemy>();
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
                if (s.D(doneSpawning) && s.D(remaining) == 0) assaulting.Set(false);
            });
        };

        // A random point along the given edge of the level bounds, at the prefab's height.
        Vector3 EdgePoint(Edge edge) {
            var b = GameState.LevelBounds;
            var y = enemyPrefab.transform.position.y;
            var x = Random.Range(b.min.x, b.max.x);
            var z = Random.Range(b.min.z, b.max.z);
            return edge switch {
                Edge.West => new Vector3(b.min.x, y, z),
                Edge.East => new Vector3(b.max.x, y, z),
                Edge.South => new Vector3(x, y, b.min.z),
                _ => new Vector3(x, y, b.max.z),
            };
        }
    }
}
