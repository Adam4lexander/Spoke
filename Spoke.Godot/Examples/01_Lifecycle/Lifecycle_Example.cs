using Godot;

namespace Spoke.Examples;

public partial class Lifecycle_Example : SpokeNode2D {

    protected override void Init(EffectBuilder s) {

        // Init is mounted when the node enters the tree, and cleaned up when the node is freed
        GD.Print("Init mounted");
        s.OnCleanup(() => GD.Print("Init disposed"));

        // Runs while the node is inside the SceneTree (this can cycle -- a reparented node leaves
        // the tree and comes back, and its Spoke tree survives the trip)
        s.Phase(IsInTree, s => {
            GD.Print("  IsInTree mounted");
            s.OnCleanup(() => GD.Print("  IsInTree disposed"));
        });

        // Runs while the node is visible -- itself and every ancestor
        s.Phase(IsShown, s => {
            GD.Print("  IsShown mounted");
            s.OnCleanup(() => GD.Print("  IsShown disposed"));
        });

        // Children are added the ordinary Godot way, and s.OnCleanup is the teardown half
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

    // Press V to toggle visibility, T to detach from the tree, F to free the node
    public override void _UnhandledKeyInput(InputEvent @event) {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode) {
            case Key.V: Visible = !Visible; break;
            case Key.T: DetachBriefly(); break;
            case Key.F: QueueFree(); break;
        }
    }

    // Detached nodes don't receive input, so the trip back is on a timer. Add/RemoveChild are
    // deferred because Godot forbids restructuring the tree while it's propagating notifications.
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
