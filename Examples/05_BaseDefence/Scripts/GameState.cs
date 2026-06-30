using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class GameState : SpokeSingleton<GameState> {

        public enum DebugModes {
            None = 0,
            Service = 1,
            Radar = 2,
            TrackedEnemy = 3,
            Buildings = 4,
        }

        [Header("Level")]
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        public Bounds LevelBounds => new Bounds(transform.position, new Vector3(dimensions.x, 0f, dimensions.y));

        [Header("Spawning")]
        [SerializeField] GameObject enemyPrefab;
        [SerializeField] float spawnInterval = 2f;

        [Header("Debug")]
        [SerializeField] UState<DebugModes> debugMode = new();
        [SerializeField] UState<Color> debugColour = new(Color.green);

        readonly CollisionWorld<Service> edgeZone = new();
        readonly CollisionWorld<Service> serviceZone = new();
        readonly CollisionWorld<Radar> radarZone = new();
        readonly CollisionWorld<Enemy> trackedEnemyZone = new();
        readonly CollisionWorld<Building> buildingZone = new();

        public static CollisionWorld<Service> EdgeZone => Instance.edgeZone;
        public static CollisionWorld<Service> ServiceZone => Instance.serviceZone;
        public static CollisionWorld<Radar> RadarZone => Instance.radarZone;
        public static CollisionWorld<Enemy> TrackedEnemyZone => Instance.trackedEnemyZone;
        public static CollisionWorld<Building> BuildingZone => Instance.buildingZone;

        protected override void Init(EffectBuilder s) {
            s.Effect(RunDebugMode);
            s.Effect(SpawnEnemies);
        }

        void LateUpdate() {
            // Tick service edges, settle connectivity, then tick the rest of the zones.
            edgeZone.Tick();
            Service.UpdateNetwork();
            serviceZone.Tick();
            radarZone.Tick();
            trackedEnemyZone.Tick();
            buildingZone.Tick();
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

        EffectBlock RunDebugMode => s => {
            var circles = s.Effect(s => {
                var mode = s.D(debugMode);
                if (mode == DebugModes.Service) return s.Effect(DebugCircles(serviceZone));
                if (mode == DebugModes.Radar) return s.Effect(DebugCircles(radarZone));
                if (mode == DebugModes.TrackedEnemy) return s.Effect(DebugCircles(trackedEnemyZone));
                if (mode == DebugModes.Buildings) return s.Effect(DebugCircles(buildingZone));
                return null;
            });

            s.Effect(s => {
                if (s.D(circles) == null) return;
                s.Effect(RangeDisplay.Draw(circles, debugColour));
            });
        };

        EffectBlock<List<Circle>> DebugCircles<T>(CollisionWorld<T> zone) => s => {
            var sensor = s.Use(zone.AddSensor(new Circle(transform.position, dimensions.magnitude)));
            return s.Memo(s => {
                var circles = new List<Circle>();
                foreach (var collider in sensor.Overlaps) circles.Add(collider.Circle);
                return circles;
            }, sensor.OverlapsChanged);
        };

        void OnDrawGizmosSelected() {
            var bounds = LevelBounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}