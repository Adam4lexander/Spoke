using System;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A Node2D whose _Draw is supplied as a delegate, so an effect can create one, point it at a
/// closure, and let cleanup take it away.
///
/// This is what replaces the Unity version's mesh building. Unity has no immediate-mode 2D drawing
/// at runtime, so CoverageDisplay and LinkDisplay there each build a GameObject, a Mesh and a
/// Material instance, and hand-write vertex and index buffers. In Godot, DrawArc and DrawLine are
/// enough — the whole of both displays reduces to "recompute the shapes, then QueueRedraw".
/// </summary>
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
