using Godot;

namespace Spoke.Examples;

/// <summary>
/// Memo&lt;T&gt; is derived state. It tracks its own dependencies and recalculates only when they
/// change. Unlike State&lt;T&gt; you can't Set() it — its value is computed.
///
/// Memos chain: labelText below depends on evenOdd, which depends on count.
///
/// UP / DOWN  change the count      SPACE  toggle casing
/// </summary>
public partial class Memo_Example : SpokeNode2D {

    // Reactive inputs.
    State<int> count = State.Create(0);
    State<bool> useUpperCase = State.Create(false);

    protected override void Init(EffectBuilder s) {

        var countLabel = AddLabel(s, 100);
        var evenOddLabel = AddLabel(s, 140);
        AddLabel(s, 40).Text = "UP / DOWN  change count      SPACE  toggle casing";

        // Recomputes whenever count changes.
        var evenOdd = s.Memo(s => s.D(count) % 2 == 0 ? "Even" : "Odd");

        // Depends on another memo *and* a state. Recomputes when either changes — but note it does
        // not recompute when count changes from 2 to 4, because evenOdd's value didn't change.
        var labelText = s.Memo(s => {
            var raw = s.D(evenOdd);
            return s.D(useUpperCase) ? raw.ToUpper() : raw.ToLower();
        });

        s.Effect(s => countLabel.Text = $"Count: {s.D(count)}");

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
            case Key.Up: count.Update(c => c + 1); break;
            case Key.Down: count.Update(c => c - 1); break;
            case Key.Space: useUpperCase.Update(b => !b); break;
        }
    }
}
