using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Sends enemies in waves: each assault pours in from one edge of the level,
    // and each wave is bigger and faster than the last, with a lull in between.
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

        public ISignal<int> Wave => wave;
        public ISignal<bool> IsAssaulting => assaulting;

        protected override void Init(EffectBuilder s) {
            var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);
            s.Phase(isPlaying, s => {
                var isLull = s.Memo(s => !s.D(assaulting));
                s.Phase(isLull, Lull);
                s.Phase(assaulting, Assault);
            });
        }

        EffectBlock Lull => s => {
            IEnumerator onUpdate() {
                yield return new WaitForSeconds(lullDuration);
                wave.Update(x => x + 1);
                assaulting.Set(true);
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        EffectBlock Assault => s => {
            var waveNow = s.D(wave);
            var count = baseCount + countPerWave * (waveNow - 1);
            var interval = Mathf.Max(minSpawnInterval, baseSpawnInterval - spawnIntervalStep * (waveNow - 1));
            var edge = Random.Range(0, 4);   // this wave's front

            IEnumerator onUpdate() {
                yield return null;
                for (int i = 0; i < count; i++) {
                    Pool.Spawn(enemyPrefab, EdgePoint(edge), Quaternion.identity);
                    yield return new WaitForSeconds(interval);
                }
                assaulting.Set(false);
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        // A random point along the given edge of the level bounds, at the prefab's height.
        Vector3 EdgePoint(int edge) {
            var b = GameState.LevelBounds;
            var y = enemyPrefab.transform.position.y;
            var x = Random.Range(b.min.x, b.max.x);
            var z = Random.Range(b.min.z, b.max.z);
            return edge switch {
                0 => new Vector3(b.min.x, y, z),  // west
                1 => new Vector3(b.max.x, y, z),  // east
                2 => new Vector3(x, y, b.min.z),  // south
                _ => new Vector3(x, y, b.max.z),  // north
            };
        }
    }
}
