using System;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A centre-and-radius circle in world space. The whole game is overlapping circles: power
/// coverage, radar coverage, turret range, repair range, unit footprints.
/// </summary>
public readonly struct Circle : IEquatable<Circle> {

    public readonly Vector2 Center;
    public readonly float Radius;

    public Circle(Vector2 center, float radius) {
        Center = center;
        Radius = radius;
    }

    /// <summary>True when the circles intersect or one contains the other. Exactly touching doesn't count.</summary>
    public bool Overlaps(Circle other) {
        var reach = Radius + other.Radius;
        return Center.DistanceSquaredTo(other.Center) < reach * reach;
    }

    public bool Equals(Circle other) => Center == other.Center && Radius == other.Radius;
    public override bool Equals(object obj) => obj is Circle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Center, Radius);
    public static bool operator ==(Circle a, Circle b) => a.Equals(b);
    public static bool operator !=(Circle a, Circle b) => !a.Equals(b);
}
