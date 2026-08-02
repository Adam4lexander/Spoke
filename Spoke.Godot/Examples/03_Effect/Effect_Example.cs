using Godot;

namespace Spoke.Examples;

/// <summary>
/// The three block types, side by side. All of them re-run when a dependency changes; they differ
/// in when they first mount.
///
///   Effect    mounts immediately, re-runs on every trigger
///   Phase     mounts only while a condition is true
///   Reaction  doesn't mount until a trigger fires
///
/// Each block flashes its swatch green and fades to blue while mounted, and turns it red on
/// disposal — so you can see exactly which blocks re-ran.
///
/// SPACE  fire the trigger      1  toggle the outer phase      2  toggle the inner phase
/// </summary>
public partial class Effect_Example : SpokeNode2D {

    State<bool> mountOuterPhase = State.Create(true);
    State<bool> mountInnerPhase = State.Create(true);

    // Godot exports *properties*, so a reactive value reaches the Inspector as a two-line pair.
    // This is what replaces Unity's UState<T> — no wrapper type, no custom PropertyDrawer, and
    // editing the value in the Inspector drives the reactive graph like any other Set().
    [Export] public bool MountOuterPhase { get => mountOuterPhase.Now; set => mountOuterPhase.Set(value); }
    [Export] public bool MountInnerPhase { get => mountInnerPhase.Now; set => mountInnerPhase.Set(value); }

    // A Trigger is Spoke's simplest signal: a fire-and-forget pulse with no value attached.
    Trigger flashCommand = Trigger.Create();

    protected override void Init(EffectBuilder s) {

        AddLabel(s, 40, "SPACE  fire trigger      1  toggle outer phase      2  toggle inner phase");

        var effect = Row(s, 0);
        var outer = Row(s, 1);
        var inner = Row(s, 2);
        var reaction = Row(s, 3);

        // Mounts immediately, and re-runs every time flashCommand fires.
        s.Effect(Flash("Effect", effect), flashCommand);

        // Mounts only while mountOuterPhase is true, and re-runs on flashCommand while mounted.
        s.Phase(mountOuterPhase, s => {

            // Nested inside the phase, so it mounts and disposes with it.
            s.Effect(Flash("Phase (Outer)", outer));

            // A phase within a phase — mounted only when both conditions hold.
            s.Phase(mountInnerPhase, Flash("Phase (Inner)", inner), flashCommand);

        }, flashCommand);

        // Stays unmounted until flashCommand fires for the first time.
        s.Reaction(Flash("Reaction", reaction), flashCommand);
    }

    // An EffectBlock is the `s => { ... }` you pass to Effect/Phase/Reaction. Pulling it out into a
    // method that *returns* one lets it be parameterised and reused, and keeps Init readable.
    EffectBlock Flash(string name, (ColorRect Swatch, Label Text) row) => s => {

        row.Text.Text = $"{name} — mounted";
        row.Swatch.Color = Colors.Green;

        // Unity's version of this example drove the flash with a coroutine whose lifetime Spoke
        // managed. Godot C# has no coroutines; s.OnProcess is the equivalent, and it stops when
        // this block disposes — including mid-fade.
        var elapsed = 0.0;
        s.OnProcess(delta => {
            elapsed += delta;
            var t = (float)Mathf.Min(elapsed / 0.5, 1.0);
            row.Swatch.Color = Colors.Green.Lerp(Colors.Blue, t);
        });

        s.OnCleanup(() => {
            // The row itself may already be gone if the whole node is tearing down.
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

    public override void _UnhandledKeyInput(InputEvent @event) {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode) {
            case Key.Space: flashCommand.Invoke(); break;
            case Key.Key1: mountOuterPhase.Update(v => !v); break;
            case Key.Key2: mountInnerPhase.Update(v => !v); break;
        }
    }
}
