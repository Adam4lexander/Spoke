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
        }

        [Header("Level")]
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        public Bounds LevelBounds => new Bounds(transform.position, new Vector3(dimensions.x, 0f, dimensions.y));

        [Header("Debug")]
        [SerializeField] UState<DebugModes> debugMode = new();
        [SerializeField] UState<Color> debugColour = new(Color.green);

        readonly CollisionWorld<Service> serviceZone = new();
        readonly CollisionWorld<Radar> radarZone = new();
        readonly CollisionWorld<Enemy> trackedEnemyZone = new();

        public static CollisionWorld<Service> ServiceZone => Instance.serviceZone;
        public static CollisionWorld<Radar> RadarZone => Instance.radarZone;
        public static CollisionWorld<Enemy> TrackedEnemyZone => Instance.trackedEnemyZone;

        protected override void Init(EffectBuilder s) {
            s.Effect(RunDebugMode);
        }

        void LateUpdate() {
            serviceZone.Tick();
            radarZone.Tick();
            trackedEnemyZone.Tick();
        }

        EffectBlock RunDebugMode => s => {
            var circles = s.Effect(s => {
                var mode = s.D(debugMode);
                if (mode == DebugModes.Service) return s.Effect(DebugCircles(serviceZone));
                if (mode == DebugModes.Radar) return s.Effect(DebugCircles(radarZone));
                if (mode == DebugModes.TrackedEnemy) return s.Effect(DebugCircles(trackedEnemyZone));
                return null;
            });

            s.Effect(s => {
                if (s.D(circles) == null) return;
                s.Effect(RangeDisplay.Draw(circles, debugColour));
            });
        };

        EffectBlock<List<Circle>> DebugCircles<T>(CollisionWorld<T> zone) => s => {
            var sensor = s.Use(zone.AddSensor(new Circle(Vector3.zero, float.PositiveInfinity)));
            return s.Memo(s => {
                var circles = new List<Circle>();
                foreach (var collider in sensor.Overlaps) circles.Add(collider.Circle);
                return circles;
            }, sensor.Changed);
        };

        void OnDrawGizmosSelected() {
            var bounds = LevelBounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}