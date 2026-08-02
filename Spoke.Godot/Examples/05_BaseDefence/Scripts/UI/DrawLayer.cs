using System;
using Godot;

namespace Spoke.Examples.BaseDefence;

// A Node2D whose _Draw is supplied as a delegate, so an effect can create one, point it at a
// closure, and let cleanup take it away. Replaces the mesh and material the Unity displays build
// by hand, since Godot draws arcs and lines directly.
public partial class DrawLayer : Node2D {

    /// <summary>Called from _Draw. Call Refresh() after changing anything it reads.</summary>
    public Action<DrawLayer> OnDraw;

    public DrawLayer(int zIndex = 0) {
        ZIndex = zIndex;
        ZAsRelative = false;
    }

    /// <summary>Schedules a repaint for the end of the frame.</summary>
    public void Refresh() => QueueRedraw();

    public override void _Draw() => OnDraw?.Invoke(this);
}
