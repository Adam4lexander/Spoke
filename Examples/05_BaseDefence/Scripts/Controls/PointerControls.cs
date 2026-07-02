using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Shows the ranges of whatever building is under the mouse. Lives on the Main Camera (like
    // CameraControls) since it's input/presentation, not game state. The range circles are gathered
    // only within the current camera view.
    public class PointerControls : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] UState<Color> hoverColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> highlightColour = new(Color.cyan);

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                var hovered = s.Effect(Hovered);

                s.Effect(s => {
                    var hoveredNow = s.D(hovered);
                    if (hoveredNow == null) return;

                    var go = hoveredNow.Owner;
                    var power = go.GetComponent<PowerNode>();
                    if (power != null && !power.IsLeaf) s.Effect(Coverage(GameState.PowerZone, body => body.IsProvider));
                    else if (go.GetComponent<Radar>() != null) s.Effect(Coverage(GameState.RadarZone));
                    else if (go.GetComponent<Turret>() != null) s.Effect(Coverage(GameState.TurretZone));

                    var ring = s.Memo(s => {
                        var circle = hoveredNow.Circle;
                        var larger = new Circle(circle.Center, circle.Radius * 1.5f);
                        return new List<Circle> { larger };
                    });
                    s.Effect(AreaDisplay.Draw(ring, highlightColour));

                    s.Effect(PowerLinks(hoveredNow.Owner.GetComponent<PowerNode>()));
                });
            });
        }

        // The ground unit currently under the mouse cursor (or null) — a point sensor that follows
        // the mouse across the ground plane. Overlaps are nearest-first, so [0] is the unit under
        // the cursor. Nothing is hovered while the cursor points at no ground (over UI, or its ray
        // misses the plane); the sensor holds its last point through those gaps.
        EffectBlock<ICollider<GameObject>> Hovered => s => {
            var point = default(Circle);
            Circle mousePoint() {
                var mp = GameState.View.Now.MousePoint;
                if (mp != null) point = new Circle(mp.Value, 0f);
                return point;
            }
            var sensor = s.Use(GameState.GroundZone.AddSensor(mousePoint));
            return s.Memo(s => s.D(GameState.View).MousePoint != null && sensor.Overlaps.Count > 0 ? sensor.Overlaps[0] : null, sensor.OverlapsChanged);
        };

        // Shows the hovered node's parent chain up to the root — a line from each node to the
        // provider powering it. Re-walks whenever any parent along the chain changes.
        EffectBlock PowerLinks(PowerNode start) => s => {
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
