using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Draws the hovered unit's in-world overlays: a highlight ring, its coverage circles, and its
    // power links. The coverage circles are gathered only within the current camera view.
    public class HoverOverlay : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] UState<Color> hoverColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> highlightColour = new(Color.cyan);

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(s => {
                    var hoveredNow = s.D(GameState.Hovered);
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
