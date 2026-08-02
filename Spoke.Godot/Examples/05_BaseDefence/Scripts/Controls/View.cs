using System;
using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>A per-frame snapshot of the camera's view of the board.</summary>
public readonly struct View : IEquatable<View> {

    /// <summary>Where the camera is looking, in world space.</summary>
    public readonly Vector2 Center;

    /// <summary>Bounding circle of the board the camera can see. Coverage overlays query with it.</summary>
    public readonly Circle VisibleArea;

    /// <summary>Where the cursor is on the board, or null while it's over the UI.</summary>
    public readonly Vector2? MousePoint;

    public View(Vector2 center, Circle visibleArea, Vector2? mousePoint) {
        Center = center;
        VisibleArea = visibleArea;
        MousePoint = mousePoint;
    }

    public bool Equals(View other)
        => Center == other.Center
        && VisibleArea == other.VisibleArea
        && EqualityComparer<Vector2?>.Default.Equals(MousePoint, other.MousePoint);

    public override bool Equals(object obj) => obj is View view && Equals(view);
    public override int GetHashCode() => HashCode.Combine(Center, VisibleArea, MousePoint);
    public static bool operator ==(View a, View b) => a.Equals(b);
    public static bool operator !=(View a, View b) => !a.Equals(b);
}
