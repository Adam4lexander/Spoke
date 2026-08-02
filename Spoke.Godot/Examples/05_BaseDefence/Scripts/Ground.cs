using Godot;

namespace Spoke.Examples.BaseDefence;

// The board itself: the play area, its grid, and the ground beyond its edge. Nothing reactive
// about it, so it's a plain Node2D rather than a Spoke node.
//
// [Tool] so it draws in the editor too, where the level is arranged. Safe here precisely because
// there's no Init -- [Tool] on a Spoke node would spawn its tree and run the game in the editor.
[Tool]
public partial class Ground : Node2D {

    float gridStep = 5f;
    Vector2 lastDimensions;

    /// <summary>Grid spacing in metres.</summary>
    [Export]
    public float GridStep {
        get => gridStep;
        set {
            gridStep = Mathf.Max(0.5f, value);
            QueueRedraw();
        }
    }

    // The play area is GameState's to define, so read it rather than keeping a copy.
    Vector2 Dimensions => Owner is GameState game ? game.Dimensions : new Vector2(40f, 40f);

    public override void _Process(double delta) {
        // Editor only: keep the preview live when Dimensions is edited on the root.
        if (!Engine.IsEditorHint()) return;
        if (Dimensions == lastDimensions) return;
        lastDimensions = Dimensions;
        QueueRedraw();
    }

    public override void _Draw() {
        var bounds = GameState.BoundsOf(Dimensions);

        // The surround, so panning to the rim doesn't show the void.
        DrawRect(bounds.Grow(World.Px(30f)), Palette.SubGround);
        DrawRect(bounds, Palette.Ground);

        var step = World.Px(GridStep);
        for (var x = bounds.Position.X; x <= bounds.End.X + 0.5f; x += step) {
            DrawLine(new Vector2(x, bounds.Position.Y), new Vector2(x, bounds.End.Y), Palette.Grid, 1f);
        }
        for (var y = bounds.Position.Y; y <= bounds.End.Y + 0.5f; y += step) {
            DrawLine(new Vector2(bounds.Position.X, y), new Vector2(bounds.End.X, y), Palette.Grid, 1f);
        }

        DrawRect(bounds, Palette.GroundEdge, filled: false, width: 4f);
    }
}
