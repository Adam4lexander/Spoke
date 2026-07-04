using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class EnemySpawner : SpokeBehaviour {

        [SerializeField] GameObject enemyPrefab;
        [SerializeField] float spawnInterval = 2f;

        protected override void Init(EffectBuilder s) {
            s.Effect(SpawnEnemies);
        }

        EffectBlock SpawnEnemies => s => {
            IEnumerator onUpdate() {
                while (true) {
                    yield return new WaitForSeconds(spawnInterval);
                    // A random point on the perimeter of the level bounds, at the prefab's height.
                    var b = GameState.LevelBounds;
                    var y = enemyPrefab.transform.position.y;
                    var x = Random.Range(b.min.x, b.max.x);
                    var z = Random.Range(b.min.z, b.max.z);
                    var edge = Random.Range(0, 4) switch {
                        0 => new Vector3(b.min.x, y, z),  // west
                        1 => new Vector3(b.max.x, y, z),  // east
                        2 => new Vector3(x, y, b.min.z),  // south
                        _ => new Vector3(x, y, b.max.z),  // north
                    };
                    Pool.Spawn(enemyPrefab, edge, Quaternion.identity);
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };
    }
}
