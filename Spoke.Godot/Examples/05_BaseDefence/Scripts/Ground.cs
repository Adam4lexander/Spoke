using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The board itself: the play area, its grid, and the ground beyond its edge.
///
/// Nothing reactive about it, so it isn't a Spoke node — a plain Node2D with _Draw. The rest of the
/// integration doesn't mind: extending SpokeNode2D is for nodes that want an Init, not a tax on
/// every node in the scene.
///
/// [Tool] so it draws in the editor as well as in the game. Without it the level is invisible while
/// you're arranging the scene, which rather defeats the point of the scene holding the level. Note
/// that the attribute is safe here precisely because this node has no Init — putting [Tool] on a
/// Spoke node would spawn its tree in the editor and run the game there.
/// </summary>
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

    // The play area is GameState's to define, so this reads it rather than keeping its own copy.
    // Owner is the scene root both in the editor and at runtime, and an exported value is applied
    // by the loader whether or not the script it's on is [Tool].
    Vector2 Dimensions => Owner is GameState game ? game.Dimensions : new Vector2(40f, 40f);

    public override void _Process(double delta) {
        // Editor only: keep the preview live when Dimensions is edited on the root. The game never
        // changes it, so this costs nothing at runtime.
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
