using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class GameState : SpokeSingleton<GameState> {

        [Header("Level")]
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        [Header("Spawning")]
        [SerializeField] GameObject enemyPrefab;
        [SerializeField] float spawnInterval = 2f;

        State<View> view = new();
        public static ISignal<View> View => Instance.view;

        CollisionWorld<PowerBody> powerZone = new();
        CollisionWorld<GameObject> groundZone = new();
        CollisionWorld<Radar> radarZone = new();
        CollisionWorld<Turret> turretZone = new();
        CollisionWorld<Enemy> trackedEnemyZone = new();

        public static CollisionWorld<PowerBody> PowerZone => Instance.powerZone;
        public static CollisionWorld<GameObject> GroundZone => Instance.groundZone;
        public static CollisionWorld<Radar> RadarZone => Instance.radarZone;
        public static CollisionWorld<Turret> TurretZone => Instance.turretZone;
        public static CollisionWorld<Enemy> TrackedEnemyZone => Instance.trackedEnemyZone;

        public static Plane GroundPlane => new(Vector3.up, LevelBounds.center);

        public static Bounds LevelBounds {
            get {
                var inst = Instance;
                return new Bounds(inst.transform.position, new Vector3(inst.dimensions.x, 0f, inst.dimensions.y));
            }
        }

        public static void RecomputeView(Camera camera) => Instance.view.Set(new View(camera, GroundPlane));

        State<float> money = new();
        public static IState<float> Money => Instance.money;

        State<int> collectRate = new();
        public static IState<int> CollectRate => Instance.collectRate;

        protected override void Init(EffectBuilder s) {
            s.Effect(SpawnEnemies);
        }

        void LateUpdate() {
            powerZone.Tick();
            groundZone.Tick();
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