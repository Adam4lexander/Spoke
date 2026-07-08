using System;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    /// <summary>A center-and-radius circle on the XZ (ground) plane.</summary>
    public readonly struct Circle : IEquatable<Circle> {

        /// <summary>World-space anchor, usually a transform.position. Only X and Z define the circle; Y is ignored by Overlaps.</summary>
        public readonly Vector3 Center;
        /// <summary>Radius, in world units.</summary>
        public readonly float Radius;

        public Circle(Vector3 center, float radius) {
            Center = center;
            Radius = radius;
        }

        /// <summary>True when the two circles intersect or one contains the other, measured on the XZ plane. Exactly touching doesn't count.</summary>
        public bool Overlaps(Circle other) {
            var reach = Radius + other.Radius;
            var dx = Center.x - other.Center.x;
            var dz = Center.z - other.Center.z;
            return dx * dx + dz * dz < reach * reach;
        }

        /// <summary>Draws the outline flat on the XZ plane, for use from OnDrawGizmos.</summary>
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
