using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Pans the board with WASD, and publishes the resulting View each frame.
///
/// The Unity version needs a ground plane and four corner raycasts to work out what the camera can
/// see and where the mouse points. In 2D there's nothing to project: the camera transform already
/// maps screen to world, so GetGlobalMousePosition is the answer, and the visible rectangle is the
/// viewport size in world units.
/// </summary>
public partial class CameraControls : SpokeNode2D {

    // Both serialized on CameraControls in BaseDefence.unity.
    const float PanSpeed = 20f;       // metres per second at full tilt
    const float Acceleration = 16f;   // how sharply velocity chases the input

    /// <summary>
    /// How much board fits on screen, in metres across.
    ///
    /// The Unity rig is a perspective camera, so there's no zoom value to copy — but there is one
    /// to derive. It sits at (0, 18.71, -6.99) pitched 70 degrees, which puts the point it looks at
    /// 19.91m down its view axis, near the origin. With a 60-degree vertical FOV at 16:9 the ground
    /// spans 2 * 19.91 * tan(30) * (16/9) across the centre of frame:
    ///
    ///     2 * 19.91 * 0.5774 * 1.7778 = 40.9m
    ///
    /// This is set a little tighter than that. Godot's default 1152x648 window is smaller than the
    /// view the Unity game was framed for, and at the full derived width the units get hard to
    /// read. Set it back to 40.9 to frame exactly what the original does.
    /// </summary>
    [Export] public float ViewWidth { get; set; } = 36f;

    readonly State<View> view = State.Create(default(View));

    /// <summary>The camera's current view of the board, updated each frame.</summary>
    public ISignal<View> View => view;

    Camera2D camera;

    protected override void Init(EffectBuilder s) {

        // Always, so the board still pans while the game is paused — reading the map during the
        // pregame briefing or after a loss is part of the game.
        ProcessMode = ProcessModeEnum.Always;

        var bounds = GameState.LevelBounds;
        camera = GetNode<Camera2D>("Camera2D");

        var zoom = GetViewportRect().Size.X / World.Px(ViewWidth);
        camera.Zoom = new Vector2(zoom, zoom);

        // Deliberately not Camera2D's Limit properties. Those keep the *view* inside the board,
        // which is a different rule from the one Unity uses: it clamps the point the camera looks
        // at, letting the edge of the map sit mid-screen. Limits also decouple where the camera is
        // from what it shows, so pressing into an edge banks up position the view never used, and
        // reversing spends it before anything moves. Clamping the position, below, is the whole job.

        // Init runs as this node enters the tree, which is before its own children enter it, and a
        // Camera2D refuses to become current until it's in the tree. This is what IsReady is for.
        s.Phase(IsReady, s => camera.MakeCurrent());

        var velocity = Vector2.Zero;

        s.OnProcess(delta => {
            var input = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W)) input += Vector2.Up;
            if (Input.IsKeyPressed(Key.S)) input += Vector2.Down;
            if (Input.IsKeyPressed(Key.A)) input += Vector2.Left;
            if (Input.IsKeyPressed(Key.D)) input += Vector2.Right;

            // Ease velocity towards the input so it ramps up on press and glides to a stop on
            // release. The Exp keeps the easing frame-rate independent.
            var target = input.Normalized() * World.Px(PanSpeed);
            velocity = velocity.Lerp(target, 1f - Mathf.Exp(-Acceleration * (float)delta));

            // Same two lines as the Unity version: move the looked-at point, then keep it on the
            // board. Nothing accumulates past the clamp, so reversing at an edge moves immediately.
            var next = camera.Position + velocity * (float)delta;
            camera.Position = new Vector2(
                Mathf.Clamp(next.X, bounds.Position.X, bounds.End.X),
                Mathf.Clamp(next.Y, bounds.Position.Y, bounds.End.Y));

            view.Set(Sample());
        });

        // Publish an initial view so anything reading it on its first run sees something real.
        view.Set(Sample());
    }

    View Sample() {
        var center = camera.GlobalPosition;
        var extent = GetViewportRect().Size / camera.Zoom * 0.5f;
        var visible = new Circle(center, extent.Length());

        // Godot tells us directly whether a Control is under the pointer. Unity needs
        // EventSystem.IsPointerOverGameObject for the same question.
        Vector2? mouse = GetViewport().GuiGetHoveredControl() != null ? null : GetGlobalMousePosition();
        if (mouse.HasValue && !GameState.LevelBounds.HasPoint(mouse.Value)) mouse = null;

        return new View(center, visible, mouse);
    }
}
