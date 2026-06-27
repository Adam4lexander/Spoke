using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class GameState : SpokeSingleton<GameState> {

        public enum DebugModes {
            None = 0,
            Service = 1,
            Radar = 2,
        }

        [Header("Level")]
        [SerializeField] Vector2 dimensions = new Vector2(40f, 40f);

        public Bounds LevelBounds => new Bounds(transform.position, new Vector3(dimensions.x, 0f, dimensions.y));

        [Header("Debug")]
        [SerializeField] UState<DebugModes> debugMode = new();
        [SerializeField] UState<Color> debugColour = new(Color.green);

        public SpatialIndex<Service> ServiceZone { get; } = new();
        public SpatialIndex<Radar> RadarZone { get; } = new();

        protected override void Init(EffectBuilder s) {
            s.Effect(RunDebugMode);
        }

        EffectBlock RunDebugMode => s => {
            var circles = s.Effect(s => {
                var debugModeNow = s.D(debugMode);
                if (debugModeNow == DebugModes.Service) {
                    var watch = s.Use(ServiceZone.Watch(new Circle(Vector3.zero, float.PositiveInfinity)));
                    return s.Memo(s => {
                        var list = new List<Circle>();
                        foreach (var entry in s.D(watch.Items)) list.Add(entry.Circle);
                        return list;
                    });
                }
                if (debugModeNow == DebugModes.Radar) {
                    var watch = s.Use(RadarZone.Watch(new Circle(Vector3.zero, float.PositiveInfinity)));
                    return s.Memo(s => {
                        var list = new List<Circle>();
                        foreach (var entry in s.D(watch.Items)) list.Add(entry.Circle);
                        return list;
                    });
                }
                return null;
            });

            s.Effect(s => {
                if (s.D(circles) == null) return;
                s.Effect(RangeDisplay.Draw(circles, debugColour));
            });
        };

        void OnDrawGizmosSelected() {
            var bounds = LevelBounds;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}