using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Reactive recipes for the in-world overlays — coverage circles, highlight rings, power links —
    // drawn in a shared palette. Provides EffectBlocks; callers decide which to mount.
    public class Overlays : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] UState<Color> hoverColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> highlightColour = new(Color.cyan);

        protected override void Init(EffectBuilder s) { }

        public EffectBlock PowerCoverage => Coverage(GameState.PowerZone, body => body.IsProvider);
        public EffectBlock RadarCoverage => Coverage(GameState.RadarZone);
        public EffectBlock TurretCoverage => Coverage(GameState.TurretZone);

        // A highlight ring around the unit, slightly larger than its footprint.
        public EffectBlock UnitRing(ICollider<GameObject> unit) => s => {
            var ring = s.Memo(s => {
                var circle = unit.Circle;
                var larger = new Circle(circle.Center, circle.Radius * 1.5f);
                return new List<Circle> { larger };
            });
            s.Effect(AreaDisplay.Draw(ring, highlightColour));
        };

        // Shows the node's parent chain up to the root — a line from each node to the provider
        // powering it. Re-walks whenever any parent along the chain changes.
        public EffectBlock PowerLinks(PowerNode start) => s => {
            var segments = s.Memo(s => {
                var list = new List<(Vector3 from, Vector3 to)>();
                var node = start;
                while (node != null) {
                    var parent = s.D(node.Parent);
                    if (parent == null) break;
                    list.Add((node.transform.position, parent.transform.position));
                    node = parent;
                }
                return list;
            });
            s.Effect(LinkDisplay.Draw(segments, highlightColour));
        };

        // Shows a zone's coverage circles within the camera's ground view (a sensor sized to that
        // view, recentred as it changes; dedups when stationary). The filter, if given, keeps only
        // matching colliders.
        EffectBlock Coverage<T>(CollisionWorld<T> zone, Func<T, bool> filter = null) => s => {
            var sensor = s.Use(zone.AddSensor(() => GameState.View.Now.GroundArea, filter));
            var circles = s.Memo(s => {
                var list = new List<Circle>();
                foreach (var collider in sensor.Overlaps) list.Add(collider.Circle);
                return list;
            }, sensor.OverlapsChanged);
            s.Effect(AreaDisplay.Draw(circles, hoverColour));
        };
    }
}
