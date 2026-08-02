using Godot;

namespace Spoke.Examples;

/// <summary>
/// Godot's lifecycle windows, expressed as Spoke phases.
///
/// Init mounts as the node enters the tree, and disposes when it is freed. Everything else is a
/// Phase: a block
/// that mounts while a condition holds, and disposes the moment it stops. Setup and teardown sit
/// next to each other instead of in separate callbacks.
///
/// Run the scene and watch the Output panel while you press the keys.
/// </summary>
public partial class Lifecycle_Example : SpokeNode2D {

    protected override void Init(EffectBuilder s) {

        // Mounted on entering the tree, disposed when the node is freed.
        GD.Print("Init mounted");
        s.OnCleanup(() => GD.Print("Init disposed"));

        // Runs while the node is inside the SceneTree.
        // Unlike Unity's Awake/OnDestroy this can cycle any number of times — a reparented node
        // leaves the tree and comes back, and its Spoke tree survives the trip.
        s.Phase(IsInTree, s => {
            GD.Print("  IsInTree mounted");
            s.OnCleanup(() => GD.Print("  IsInTree disposed"));
        });

        // Runs while the node is visible — itself and every ancestor.
        s.Phase(IsShown, s => {
            GD.Print("  IsShown mounted");
            s.OnCleanup(() => GD.Print("  IsShown disposed"));
        });

        // Inside a Spoke node `this` is the node, so children are added the ordinary Godot way.
        // s.OnCleanup is the teardown half — the two together are the whole pattern.
        var label = new Label {
            Position = new Vector2(40, 40),
            Text = "V   toggle visible\n"
                 + "T   detach from the tree for 1.5s\n"
                 + "F   free this node\n\n"
                 + "Watch the Output panel."
        };
        AddChild(label);
        s.OnCleanup(() => label.QueueFree());
    }

    // SpokeNode2D only overrides _Notification, so every other virtual is still yours.
    public override void _UnhandledKeyInput(InputEvent @event) {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode) {
            case Key.V: Visible = !Visible; break;
            case Key.T: DetachBriefly(); break;
            case Key.F: QueueFree(); break;
        }
    }

    // Leaving the tree stops input reaching this node, so the trip back is on a timer rather than
    // a second keypress. AddChild/RemoveChild are deferred because Godot forbids restructuring the
    // tree while it is propagating notifications.
    async void DetachBriefly() {
        var parent = GetParent();
        var tree = GetTree();
        GD.Print("[detaching...]");
        parent.CallDeferred(Node.MethodName.RemoveChild, this);
        await tree.ToSignal(tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        GD.Print("[reattaching...]");
        parent.CallDeferred(Node.MethodName.AddChild, this);
    }
}
