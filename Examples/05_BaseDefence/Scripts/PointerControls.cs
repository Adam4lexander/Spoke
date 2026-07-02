using System.Collections;
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
                var groundArea = s.Memo(s => s.D(GameState.View).GroundArea);

                var circles = s.Effect(s => {
                    var hoveredNow = s.D(hovered);
                    if (hoveredNow == null) return null;
                    var go = hoveredNow.Owner;
                    var power = go.GetComponent<PowerNode>();
                    if (power != null && !power.IsLeaf) return s.Effect(ProviderCircles(groundArea));
                    if (go.GetComponent<Radar>() != null) return s.Effect(ZoneCircles(GameState.RadarZone, groundArea));
                    if (go.GetComponent<Turret>() != null) return s.Effect(ZoneCircles(GameState.TurretZone, groundArea));
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

        // The ground unit currently under the mouse cursor (or null).
        EffectBlock<ICollider<GameObject>> Hovered => s => {
            var hovered = State.Create<ICollider<GameObject>>();
            var plane = GameState.GroundPlane;
            var hits = new List<ICollider<GameObject>>();
            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    ICollider<GameObject> found = null;
                    var ray = cam.ScreenPointToRay(Input.mousePosition);
                    if (plane.Raycast(ray, out var enter)) {
                        hits.Clear();
                        GameState.GroundZone.Query(new Circle(ray.GetPoint(enter), 0f), hits);
                        if (hits.Count > 0) found = hits[0];
                    }
                    hovered.Set(found);
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
            return hovered;
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

        // The provider (coverage) ranges within the given area — a sensor over the power world,
        // keeping only the colliders that represent a Provider.
        EffectBlock<List<Circle>> ProviderCircles(ISignal<Circle> area) => s => {
            var sensor = s.Use(GameState.PowerZone.AddSensor(() => area.Now));
            return s.Memo(s => {
                var circles = new List<Circle>();
                foreach (var c in sensor.Overlaps) if (c.Owner.IsProvider) circles.Add(c.Circle);
                return circles;
            }, sensor.OverlapsChanged);
        };

        // All of a zone's range circles within the given area (a sensor sized to that area, recentred
        // as it changes; dedups when stationary).
        EffectBlock<List<Circle>> ZoneCircles<T>(CollisionWorld<T> zone, ISignal<Circle> area) => s => {
            var sensor = s.Use(zone.AddSensor(() => area.Now));
            return s.Memo(s => {
                var circles = new List<Circle>();
                foreach (var collider in sensor.Overlaps) circles.Add(collider.Circle);
                return circles;
            }, sensor.OverlapsChanged);
        };
    }
}
