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
                var view = s.Effect(ViewArea);

                var circles = s.Effect(s => {
                    var hoveredNow = s.D(hovered);
                    if (hoveredNow == null) return null;
                    var b = hoveredNow.Owner;
                    if (b.Service.ProvidesService) return s.Effect(ProviderCircles(view));
                    if (b.GetComponent<Radar>() != null) return s.Effect(ZoneCircles(GameState.RadarZone, view));
                    if (b.GetComponent<Turret>() != null) return s.Effect(ZoneCircles(GameState.TurretZone, view));
                    return null;
                });

                s.Effect(s => {
                    if (s.D(circles) == null) return;
                    s.Effect(RangeDisplay.Draw(circles, hoverColour));
                });

                s.Effect(s => {
                    if (s.D(hovered) == null) return;
                    var ring = s.Memo(s => {
                        var circle = s.D(hovered).Circle;
                        var larger = new Circle(circle.Center, circle.Radius * 1.5f);
                        return new List<Circle> { larger };
                    });
                    s.Effect(RangeDisplay.Draw(ring, highlightColour));
                });
            });
        }

        // The building currently under the mouse cursor (or null).
        EffectBlock<ICollider<Building>> Hovered => s => {
            var hovered = State.Create<ICollider<Building>>();
            var plane = new Plane(Vector3.up, GameState.Instance.LevelBounds.center);
            var hits = new List<ICollider<Building>>();
            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    ICollider<Building> found = null;
                    var ray = cam.ScreenPointToRay(Input.mousePosition);
                    if (plane.Raycast(ray, out var enter)) {
                        hits.Clear();
                        GameState.BuildingZone.Query(new Circle(ray.GetPoint(enter), 0f), hits);
                        if (hits.Count > 0) found = hits[0];
                    }
                    hovered.Set(found);
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
            return hovered;
        };

        // The camera's on-screen ground footprint as a circle (recentred as the camera pans).
        EffectBlock<Circle> ViewArea => s => {
            var view = State.Create<Circle>();
            var plane = new Plane(Vector3.up, GameState.Instance.LevelBounds.center);
            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    if (TryViewCircle(plane, out var vc)) view.Set(vc);
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
            return view;
        };

        // The provider (coverage) ranges within the given area — a sensor over the service world,
        // keeping only the colliders that represent a Provider.
        EffectBlock<List<Circle>> ProviderCircles(ISignal<Circle> area) => s => {
            var sensor = s.Use(GameState.ServiceZone.AddSensor(area.Now));
            s.Effect(s => sensor.Circle = s.D(area));
            return s.Memo(s => {
                var circles = new List<Circle>();
                foreach (var c in sensor.Overlaps) if (c.Owner.IsProvider) circles.Add(c.Circle);
                return circles;
            }, sensor.OverlapsChanged);
        };

        // All of a zone's range circles within the given area (a sensor sized to that area, recentred
        // as it changes; dedups when stationary).
        EffectBlock<List<Circle>> ZoneCircles<T>(CollisionWorld<T> zone, ISignal<Circle> area) => s => {
            var sensor = s.Use(zone.AddSensor(area.Now));
            s.Effect(s => sensor.Circle = s.D(area));
            return s.Memo(s => {
                var circles = new List<Circle>();
                foreach (var collider in sensor.Overlaps) circles.Add(collider.Circle);
                return circles;
            }, sensor.OverlapsChanged);
        };

        // Bounding circle of the camera's ground footprint (the four viewport corners ray-cast to the
        // play plane). Returns false if a corner ray misses the plane (keep the previous view).
        bool TryViewCircle(Plane plane, out Circle circle) {
            circle = default;
            if (!GroundPoint(plane, 0f, 0f, out var bl) || !GroundPoint(plane, 1f, 0f, out var br) ||
                !GroundPoint(plane, 0f, 1f, out var tl) || !GroundPoint(plane, 1f, 1f, out var tr)) return false;
            var center = (bl + br + tl + tr) * 0.25f;
            var radius = Mathf.Max(Mathf.Max(Vector3.Distance(center, bl), Vector3.Distance(center, br)),
                                   Mathf.Max(Vector3.Distance(center, tl), Vector3.Distance(center, tr)));
            circle = new Circle(center, radius);
            return true;
        }

        bool GroundPoint(Plane plane, float vx, float vy, out Vector3 point) {
            var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
            if (plane.Raycast(ray, out var enter)) { point = ray.GetPoint(enter); return true; }
            point = default;
            return false;
        }
    }
}
