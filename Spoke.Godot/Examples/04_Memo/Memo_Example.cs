using Godot;

namespace Spoke.Examples;

public partial class Memo_Example : SpokeNode2D {

    // Reactive input states
    State<int> count = State.Create(0);
    State<bool> useUpperCase = State.Create(false);

    protected override void Init(EffectBuilder s) {

        var countLabel = AddLabel(s, 100);
        var evenOddLabel = AddLabel(s, 140);
        AddLabel(s, 40).Text = "UP / DOWN  change count      SPACE  toggle casing";

        // Memo<T> represents derived state.
        // It automatically tracks dependencies and recalculates only when they change.
        // Unlike State<T>, you can't Set() it manually -- its value is computed.
        //
        // Here, `evenOdd` is a derived value that updates whenever `count` changes.
        var evenOdd = s.Memo(s => s.D(count) % 2 == 0 ? "Even" : "Odd");

        // This memo reacts to both `evenOdd` and `useUpperCase`
        // and computes the final label string with dynamic casing.
        var labelText = s.Memo(s => {
            var raw = s.D(evenOdd);
            return s.D(useUpperCase) ? raw.ToUpper() : raw.ToLower();
        });

        // Display the current count
        s.Effect(s => countLabel.Text = $"Count: {s.D(count)}");

        // Display the computed even/odd label
        s.Effect(s => evenOddLabel.Text = s.D(labelText));
    }

    Label AddLabel(EffectBuilder s, int y) {
        var label = new Label { Position = new Vector2(40, y) };
        AddChild(label);
        s.OnCleanup(() => label.QueueFree());
        return label;
    }

    public override void _UnhandledKeyInput(InputEvent @event) {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode) {
            // Press Up/Down to change the count, Space to toggle casing
            case Key.Up: count.Update(c => c + 1); break;
            case Key.Down: count.Update(c => c - 1); break;
            case Key.Space: useUpperCase.Update(b => !b); break;
        }
    }
}
