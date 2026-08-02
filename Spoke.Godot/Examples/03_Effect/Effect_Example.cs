using Godot;

namespace Spoke.Examples;

public partial class Effect_Example : SpokeNode2D {

    State<bool> mountOuterPhase = State.Create(true);
    State<bool> mountInnerPhase = State.Create(true);

    // Godot exports properties, so wrapping a State<T> in one makes it visible in the Inspector --
    // same reactive behavior, and editing it there drives the graph like any other Set()
    [Export] public bool MountOuterPhase { get => mountOuterPhase.Now; set => mountOuterPhase.Set(value); }
    [Export] public bool MountInnerPhase { get => mountInnerPhase.Now; set => mountInnerPhase.Set(value); }

    // Trigger is the simplest reactive signal in Spoke -- a fire-and-forget pulse.
    // When invoked, any subscribed effects or memos will run.
    Trigger flashCommand = Trigger.Create();

    protected override void Init(EffectBuilder s) {

        AddLabel(s, 40, "SPACE  fire trigger      1  toggle outer phase      2  toggle inner phase");

        var effect = Row(s, 0);
        var outer = Row(s, 1);
        var inner = Row(s, 2);
        var reaction = Row(s, 3);

        // Effect: Mounts immediately and remounts when any dependency is triggered
        s.Effect(Flash("Effect", effect), flashCommand);

        // Phase: Mounts only while `mountOuterPhase` is true. Remounts whenever any dependency triggers.
        s.Phase(mountOuterPhase, s => {

            // This effect is nested in the phase. It's mounted when the phase is mounted.
            s.Effect(Flash("Phase (Outer)", outer));

            // This inner phase only mounts while `mountInnerPhase` is true
            s.Phase(mountInnerPhase, Flash("Phase (Inner)", inner), flashCommand);

        }, flashCommand);

        // Reaction: Does not mount until a dependency is triggered.
        s.Reaction(Flash("Reaction", reaction), flashCommand);
    }

    // EffectBlock is a function delegate type given to effect/phase/reaction.
    // When you see `s.Effect(s => { })`, the `s => { }` is an EffectBlock.
    //
    // Sometimes we want to extract the EffectBlock instead of declaring them inline.
    // So it's re-usable, parameterisable, and Init won't become a huge nested structure.
    //
    // Flash is a double lambda. A function returning a function.
    // This pattern lets us return a parameterised EffectBlock, with `name` and `row`
    // parameters captured in a closure.
    EffectBlock Flash(string name, (ColorRect Swatch, Label Text) row) => s => {

        row.Text.Text = $"{name} — mounted";
        row.Swatch.Color = Colors.Green;

        // Godot C# has no coroutines, so the fade runs on s.OnProcess instead
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            var t = (float)Mathf.Min(elapsed / 0.5, 1.0);
            row.Swatch.Color = Colors.Green.Lerp(Colors.Blue, t);
        });

        // Stop the flash if this scope is cleaned up early
        s.OnCleanup(() => {
            // The row itself may already be gone if the whole node is tearing down
            if (!GodotObject.IsInstanceValid(row.Swatch)) return;
            row.Text.Text = $"{name} — disposed";
            row.Swatch.Color = Colors.Red;
        });
    };

    (ColorRect Swatch, Label Text) Row(EffectBuilder s, int index) {
        var y = 90 + index * 70;
        var swatch = new ColorRect {
            Position = new Vector2(40, y),
            Size = new Vector2(50, 50),
            Color = Colors.DimGray
        };
        var text = new Label {
            Position = new Vector2(110, y + 15),
            Text = "not mounted"
        };
        AddChild(swatch);
        AddChild(text);
        s.OnCleanup(() => { swatch.QueueFree(); text.QueueFree(); });
        return (swatch, text);
    }

    void AddLabel(EffectBuilder s, int y, string text) {
        var label = new Label { Position = new Vector2(40, y), Text = text };
        AddChild(label);
        s.OnCleanup(() => label.QueueFree());
    }

    // Press space to invoke the flash trigger, 1 and 2 to toggle the phases
    public override void _UnhandledKeyInput(InputEvent @event) {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode) {
            case Key.Space: flashCommand.Invoke(); break;
            case Key.Key1: mountOuterPhase.Update(v => !v); break;
            case Key.Key2: mountInnerPhase.Update(v => !v); break;
        }
    }
}
