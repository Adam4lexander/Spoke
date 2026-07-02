using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Shows the ranges of whatever building is under the mouse. Lives on the Main Camera (like
    // CameraControls) since it's input/presentation, not game state. The range circles are gathered
    // only within the current camera view.
    public class PointerControls : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Camera cam;

        [Header("Attributes")]
        [SerializeField] UState<Color> hoverColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> highlightColour = new(Color.cyan);

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                var hovered = s.Effect(Hovered);

                var circles = s.Effect(s => {
                    var hoveredNow = s.D(hovered);
                    if (hoveredNow == null) return null;
                    var go = hoveredNow.Owner;
                    var power = go.GetComponent<PowerNode>();
                    if (power != null && !power.IsLeaf) return s.Effect(ZoneCircles(GameState.PowerZone, c => c.Owner.IsProvider));
                    if (go.GetComponent<Radar>() != null) return s.Effect(ZoneCircles(GameState.RadarZone));
                    if (go.GetComponent<Turret>() != null) return s.Effect(ZoneCircles(GameState.TurretZone));
                    return null;
                });

                s.Effect(s => {
                    if (s.D(circles) == null) return;
                    s.Effect(AreaDisplay.Draw(circles, hoverColour));
                });

                s.Effect(s => {
                    if (s.D(hovered) == null) return;
                    var ring = s.Memo(s => {
                        var circle = s.D(hovered).Circle;
                        var larger = new Circle(circle.Center, circle.Radius * 1.5f);
                        return new List<Circle> { larger };
                    });
                    s.Effect(AreaDisplay.Draw(ring, highlightColour));
                });

                s.Effect(s => {
                    var hoveredNow = s.D(hovered);
                    if (hoveredNow == null) return;
                    var links = s.Memo(PowerLinks(hoveredNow.Owner.GetComponent<PowerNode>()));
                    s.Effect(LinkDisplay.Draw(links, highlightColour));
                });
            });
        }

        // The ground unit currently under the mouse cursor (or null) — a point sensor that follows
        // the mouse across the ground plane. Overlaps are nearest-first, so [0] is the unit under
        // the cursor. Keeps the previous point if the mouse ray misses the plane.
        EffectBlock<ICollider<GameObject>> Hovered => s => {
            var point = default(Circle);
            Circle mousePoint() {
                var ray = cam.ScreenPointToRay(Input.mousePosition);
                if (GameState.GroundPlane.Raycast(ray, out var enter)) point = new Circle(ray.GetPoint(enter), 0f);
                return point;
            }
            var sensor = s.Use(GameState.GroundZone.AddSensor(mousePoint));
            return s.Memo(s => sensor.Overlaps.Count > 0 ? sensor.Overlaps[0] : null, sensor.OverlapsChanged);
        };

        // Line segments walking the hovered node's parent chain up to the root — each node to the
        // provider powering it. Re-walks whenever any parent along the chain changes.
        MemoBlock<List<(Vector3 from, Vector3 to)>> PowerLinks(PowerNode start) => s => {
            var segments = new List<(Vector3 from, Vector3 to)>();
            var node = start;
            while (node != null) {
                var parent = s.D(node.Parent);
                if (parent == null) break;
                segments.Add((node.transform.position, parent.transform.position));
                node = parent;
            }
            return segments;
        };

        // A zone's range circles within the camera's ground view (a sensor sized to that view,
        // recentred as it changes; dedups when stationary). The filter, if given, keeps only
        // matching colliders.
        EffectBlock<List<Circle>> ZoneCircles<T>(CollisionWorld<T> zone, Func<ICollider<T>, bool> filter = null) => s => {
            var sensor = s.Use(zone.AddSensor(() => GameState.View.Now.GroundArea));
            return s.Memo(s => {
                var circles = new List<Circle>();
                foreach (var collider in sensor.Overlaps)
                    if (filter == null || filter(collider)) circles.Add(collider.Circle);
                return circles;
            }, sensor.OverlapsChanged);
        };
    }
}
