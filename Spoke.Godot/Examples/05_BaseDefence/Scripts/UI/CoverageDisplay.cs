using System;
using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// Renders the outline of the merged union of a set of circles (interior overlap arcs hidden).
// It creates and owns its own overlay node, torn down on cleanup, and redraws whenever the
// circles signal changes.
public static class CoverageDisplay {

    static readonly List<float> breaks = new();

    // Coverage circles for a zone, gathered within the camera's view (a sensor sized to that view,
    // recentred as it changes; dedups when stationary). The filter, if given, keeps only matching
    // colliders.
    public static EffectBlock Draw<T>(CollisionWorld<T> zone, Color colour, Func<T, bool> filter = null) => s => {
        var sensor = s.Use(zone.AddSensor(() => GameState.View.Now.VisibleArea, filter));
        var circles = s.Memo(s => {
            var list = new List<Circle>();
            foreach (var collider in sensor.Overlaps) list.Add(collider.Circle);
            return list;
        }, sensor.OverlapsChanged);
        s.Effect(DrawCircles(circles, colour));
    };

    // Draws the outline of a single fixed circle.
    public static EffectBlock Draw(Circle circle, Color colour) => s => {
        var circles = State.Create(new List<Circle> { circle });
        s.Effect(DrawCircles(circles, colour));
    };

    // Draws the outline of a single circle that can move or resize.
    public static EffectBlock Draw(ISignal<Circle> circle, ISignal<Color> colour) => s => {
        var circles = s.Memo(s => new List<Circle> { s.D(circle) });
        var layer = s.Own(GameState.Board, new DrawLayer(15));
        s.Effect(s => {
            var list = s.D(circles);
            var c = s.D(colour);
            layer.OnDraw = l => Outline(l, list, c);
            layer.Refresh();
        });
    };

    static EffectBlock DrawCircles(ISignal<List<Circle>> circles, Color colour) => s => {
        var layer = s.Own(GameState.Board, new DrawLayer(15));
        s.Effect(s => {
            var list = s.D(circles);
            layer.OnDraw = l => Outline(l, list, colour);
            layer.Refresh();
        });
    };

    // For each circle, split its ring at the angles where other circles cross it, then keep
    // only the arcs on the union boundary (midpoint outside every other circle). The split
    // angles are exact intersection points, so adjacent circles' arcs meet there.
    static void Outline(DrawLayer layer, List<Circle> circles, Color colour) {
        if (circles == null) return;

        for (var c = 0; c < circles.Count; c++) {
            var circle = circles[c];
            if (circle.Radius <= 0f) continue;

            breaks.Clear();
            for (var j = 0; j < circles.Count; j++) {
                if (j == c) continue;
                var other = circles[j];
                if (other.Radius <= 0f) continue;
                var delta = other.Center - circle.Center;
                var d = delta.Length();
                if (d >= circle.Radius + other.Radius) continue;             // disjoint
                if (d <= Mathf.Abs(circle.Radius - other.Radius)) continue;  // one contains the other
                var projection = (d * d + circle.Radius * circle.Radius - other.Radius * other.Radius) / (2f * d);
                var phi = Mathf.Acos(Mathf.Clamp(projection / circle.Radius, -1f, 1f));
                var axis = delta.Angle();
                breaks.Add(Wrap(axis - phi));
                breaks.Add(Wrap(axis + phi));
            }

            if (breaks.Count == 0) {
                // No crossings: the ring is wholly on the boundary or wholly buried.
                if (!InsideAnyOther(circles, c, PointOn(circle, 0f))) Arc(layer, circle, 0f, Mathf.Tau, colour);
                continue;
            }

            breaks.Sort();
            for (var k = 0; k < breaks.Count; k++) {
                var from = breaks[k];
                var to = k + 1 < breaks.Count ? breaks[k + 1] : breaks[0] + Mathf.Tau;
                if (InsideAnyOther(circles, c, PointOn(circle, (from + to) * 0.5f))) continue;
                Arc(layer, circle, from, to, colour);
            }
        }
    }

    static void Arc(DrawLayer layer, Circle circle, float from, float to, Color colour) {
        var steps = Mathf.Max(2, Mathf.CeilToInt((to - from) / Mathf.Tau * 64f));
        layer.DrawArc(circle.Center, circle.Radius, from, to, steps, colour, 1.5f, true);
    }

    static float Wrap(float angle) {
        angle %= Mathf.Tau;
        return angle < 0f ? angle + Mathf.Tau : angle;
    }

    static Vector2 PointOn(Circle circle, float angle)
        => circle.Center + Vector2.FromAngle(angle) * circle.Radius;

    static bool InsideAnyOther(List<Circle> circles, int self, Vector2 point) {
        for (var j = 0; j < circles.Count; j++) {
            if (j == self) continue;
            var other = circles[j];
            if (point.DistanceSquaredTo(other.Center) < other.Radius * other.Radius) return true;
        }
        return false;
    }
}
