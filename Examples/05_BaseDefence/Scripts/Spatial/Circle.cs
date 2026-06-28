using System;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public readonly struct Circle : IEquatable<Circle> {

        public readonly Vector3 Center;
        public readonly float Radius;

        public Circle(Vector3 center, float radius) {
            Center = center;
            Radius = radius;
        }

        public bool Overlaps(Circle other) {
            var reach = Radius + other.Radius;
            return (Center - other.Center).sqrMagnitude < reach * reach;
        }

        public Bounds Bounds => new(Center, new Vector3(Radius * 2f, 0f, Radius * 2f));

        public void DrawGizmo(Color colour) {
            const int segments = 48;
            var previousColour = Gizmos.color;
            Gizmos.color = colour;
            var prev = Center + new Vector3(Radius, 0f, 0f);
            for (var i = 1; i <= segments; i++) {
                var a = (i / (float)segments) * Mathf.PI * 2f;
                var next = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            Gizmos.color = previousColour;
        }

        public bool Equals(Circle other) {
            return Center.Equals(other.Center) && Radius.Equals(other.Radius);
        }

        public override bool Equals(object obj) {
            return obj is Circle other && Equals(other);
        }

        public override int GetHashCode() {
            var hashCode = 1861411795;
            hashCode = hashCode * -1521134295 + Center.GetHashCode();
            hashCode = hashCode * -1521134295 + Radius.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(Circle left, Circle right) => left.Equals(right);
        public static bool operator !=(Circle left, Circle right) => !left.Equals(right);
    }
}
