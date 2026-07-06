using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Renders the outline of the merged union of a set of circles (interior overlap arcs hidden),
    // as one line mesh.
    // It creates and owns its own GameObject + mesh + material, all torn down on cleanup,
    // and rebuilds the mesh whenever the circles signal changes.
    public static class CoverageDisplay {

        const int segmentsPerCircle = 48;

        // Coverage circles for a zone, gathered within the camera's ground view (a sensor sized to
        // that view, recentred as it changes; dedups when stationary). The filter, if given, keeps
        // only matching colliders.
        public static EffectBlock Draw<T>(CollisionWorld<T> zone, ISignal<Color> colour, Material material, System.Func<T, bool> filter = null) => s => {
            var sensor = s.Use(zone.AddSensor(() => GameState.View.Now.GroundArea, filter));
            var circles = s.Memo(s => {
                var list = new List<Circle>();
                foreach (var collider in sensor.Overlaps) list.Add(collider.Circle);
                return list;
            }, sensor.OverlapsChanged);
            s.Effect(DrawCircles(circles, colour, material));
        };

        public static EffectBlock Draw(Circle circle, ISignal<Color> colour, Material material) => s => {
            var circles = State.Create(new List<Circle> { circle });
            s.Effect(DrawCircles(circles, colour, material));
        };

        public static EffectBlock Draw(ISignal<Circle> circle, ISignal<Color> colour, Material material) => s => {
            var circles = s.Memo(s => new List<Circle> { s.D(circle) });
            s.Effect(DrawCircles(circles, colour, material));
        };

        static EffectBlock DrawCircles(ISignal<List<Circle>> circles, ISignal<Color> colour, Material material) => s => {

            var go = new GameObject("CoverageDisplay");
            go.transform.position = Vector3.up * 0.01f;
            s.Effect("WithSafeDestroy", WithSafeDestroy(go));

            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var mesh = new Mesh { name = "RangeArea" };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;
            s.Effect("WithSafeDestroy", WithSafeDestroy(mesh));

            var instance = new Material(material);
            meshRenderer.sharedMaterial = instance;
            s.Effect("WithSafeDestroy", WithSafeDestroy(instance));

            s.Effect(s => instance.color = s.D(colour));

            s.Effect("BuildOutline", s => BuildOutline(mesh, s.D(circles)));
        };

        static EffectBlock WithSafeDestroy(Object obj) => s => {
            s.OnCleanup(() => {
                if (obj == null) return;
                if (Application.isPlaying) Object.Destroy(obj);
                else Object.DestroyImmediate(obj);
            });
        };

        static void BuildOutline(Mesh mesh, List<Circle> circles) {
            mesh.Clear();
            var verts = new List<Vector3>();
            var lines = new List<int>();
            var breaks = new List<float>();

            // For each circle, split its ring at the angles where other circles cross it, then keep
            // only the arcs on the union boundary (midpoint outside every other circle). The split
            // angles are exact intersection points, so adjacent circles' arcs meet there.
            for (var c = 0; circles != null && c < circles.Count; c++) {
                var circle = circles[c];
                if (circle.Radius <= 0f) continue;

                breaks.Clear();
                for (var j = 0; j < circles.Count; j++) {
                    if (j == c) continue;
                    var other = circles[j];
                    if (other.Radius <= 0f) continue;
                    var delta = other.Center - circle.Center;
                    var d = delta.magnitude;
                    if (d >= circle.Radius + other.Radius) continue;             // disjoint
                    if (d <= Mathf.Abs(circle.Radius - other.Radius)) continue;  // one contains the other
                    var projection = (d * d + circle.Radius * circle.Radius - other.Radius * other.Radius) / (2f * d);
                    var phi = Mathf.Acos(Mathf.Clamp(projection / circle.Radius, -1f, 1f));
                    var axis = Mathf.Atan2(delta.z, delta.x);
                    breaks.Add(Wrap(axis - phi));
                    breaks.Add(Wrap(axis + phi));
                }

                if (breaks.Count == 0) {
                    // No crossings: the ring is wholly on the boundary or wholly buried.
                    if (!InsideAnyOther(circles, c, PointOn(circle, 0f))) EmitArc(verts, lines, circle, 0f, Mathf.PI * 2f);
                    continue;
                }

                breaks.Sort();
                for (var k = 0; k < breaks.Count; k++) {
                    var from = breaks[k];
                    var to = k + 1 < breaks.Count ? breaks[k + 1] : breaks[0] + Mathf.PI * 2f;
                    if (InsideAnyOther(circles, c, PointOn(circle, (from + to) * 0.5f))) continue;
                    EmitArc(verts, lines, circle, from, to);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetIndices(lines, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
        }

        static float Wrap(float angle) {
            const float twoPi = Mathf.PI * 2f;
            angle %= twoPi;
            return angle < 0f ? angle + twoPi : angle;
        }

        static Vector3 PointOn(Circle circle, float angle)
            => circle.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * circle.Radius;

        static void EmitArc(List<Vector3> verts, List<int> lines, Circle circle, float from, float to) {
            var steps = Mathf.Max(1, Mathf.CeilToInt((to - from) / (Mathf.PI * 2f) * segmentsPerCircle));
            var start = verts.Count;
            for (var i = 0; i <= steps; i++) verts.Add(PointOn(circle, Mathf.Lerp(from, to, i / (float)steps)));
            for (var i = 0; i < steps; i++) {
                lines.Add(start + i);
                lines.Add(start + i + 1);
            }
        }

        static bool InsideAnyOther(List<Circle> circles, int self, Vector3 point) {
            for (var j = 0; j < circles.Count; j++) {
                if (j == self) continue;
                var other = circles[j];
                if ((point - other.Center).sqrMagnitude < other.Radius * other.Radius) return true;
            }
            return false;
        }
    }
}
