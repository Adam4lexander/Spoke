using Godot;

namespace Spoke.Examples.BaseDefence;

// Pans the camera across the board with WASD, and publishes the resulting View each frame.
public partial class CameraControls : SpokeNode2D {

    const float PanSpeed = 20f;
    const float Acceleration = 16f;

    /// <summary>How much board fits on screen, in metres across.</summary>
    [Export] public float ViewWidth { get; set; } = 36f;

    readonly State<View> view = State.Create(default(View));

    /// <summary>The camera's current view of the board, updated each frame.</summary>
    public ISignal<View> View => view;

    Camera2D camera;

    protected override void Init(EffectBuilder s) {

        // Always, so the board still pans while the game is paused.
        ProcessMode = ProcessModeEnum.Always;

        var bounds = GameState.LevelBounds;
        camera = GetNode<Camera2D>("Camera2D");

        var zoom = GetViewportRect().Size.X / World.Px(ViewWidth);
        camera.Zoom = new Vector2(zoom, zoom);

        // A Camera2D refuses to become current until it's in the tree, and Init runs before this
        // node's own children enter it. IsReady is the phase that waits for them.
        s.Phase(IsReady, s => camera.MakeCurrent());

        var velocity = Vector2.Zero;

        s.OnProcess(delta => {
            // Pan the looked-at point across the board.
            var input = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W)) input += Vector2.Up;
            if (Input.IsKeyPressed(Key.S)) input += Vector2.Down;
            if (Input.IsKeyPressed(Key.A)) input += Vector2.Left;
            if (Input.IsKeyPressed(Key.D)) input += Vector2.Right;

            // Ease velocity toward the input so it ramps up on press and glides to a
            // stop on release. The Exp() keeps the easing frame-rate independent.
            var target = input.Normalized() * World.Px(PanSpeed);
            velocity = velocity.Lerp(target, 1f - Mathf.Exp(-Acceleration * (float)delta));

            // Keep the looked-at point inside the level, then place the camera.
            var next = camera.Position + velocity * (float)delta;
            camera.Position = new Vector2(
                Mathf.Clamp(next.X, bounds.Position.X, bounds.End.X),
                Mathf.Clamp(next.Y, bounds.Position.Y, bounds.End.Y));

            view.Set(Sample());
        });

        // Publish an initial view, so anything reading it on its first run sees something real.
        view.Set(Sample());
    }

    View Sample() {
        var center = camera.GlobalPosition;
        var extent = GetViewportRect().Size / camera.Zoom * 0.5f;
        var visible = new Circle(center, extent.Length());

        // No cursor point while the pointer is over UI, or off the board.
        Vector2? mouse = GetViewport().GuiGetHoveredControl() != null ? null : GetGlobalMousePosition();
        if (mouse.HasValue && !GameState.LevelBounds.HasPoint(mouse.Value)) mouse = null;

        return new View(center, visible, mouse);
    }
}
