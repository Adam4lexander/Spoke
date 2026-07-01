using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class GameState : SpokeSingleton<GameState> {

        [Header("Level")]
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        public Bounds LevelBounds => new Bounds(transform.position, new Vector3(dimensions.x, 0f, dimensions.y));

        [Header("Spawning")]
        [SerializeField] GameObject enemyPrefab;
        [SerializeField] float spawnInterval = 2f;

        readonly CollisionWorld<Service> serviceZone = new();
        readonly CollisionWorld<Radar> radarZone = new();
        readonly CollisionWorld<Turret> turretZone = new();
        readonly CollisionWorld<Enemy> trackedEnemyZone = new();
        readonly CollisionWorld<Building> buildingZone = new();

        public static CollisionWorld<Service> ServiceZone => Instance.serviceZone;
        public static CollisionWorld<Radar> RadarZone => Instance.radarZone;
        public static CollisionWorld<Turret> TurretZone => Instance.turretZone;
        public static CollisionWorld<Enemy> TrackedEnemyZone => Instance.trackedEnemyZone;
        public static CollisionWorld<Building> BuildingZone => Instance.buildingZone;

        protected override void Init(EffectBuilder s) {
            s.Effect(SpawnEnemies);
        }

        void LateUpdate() {
            // Building world first so the service tree settles connectivity, then the rest of the zones.
            buildingZone.Tick();
            serviceZone.Tick();
            radarZone.Tick();
            turretZone.Tick();
            trackedEnemyZone.Tick();
        }

        EffectBlock SpawnEnemies => s => {
            IEnumerator onUpdate() {
                while (true) {
                    yield return new WaitForSeconds(spawnInterval);
                    // A random point on the perimeter of the level bounds, at the prefab's height.
                    var b = LevelBounds;
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

        void OnDrawGizmosSelected() {
            var bounds = LevelBounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}