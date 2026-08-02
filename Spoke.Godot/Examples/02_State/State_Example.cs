using Godot;

namespace Spoke.Examples;

/// <summary>
/// State&lt;T&gt; is a reactive variable: read it, set it, subscribe to it. Blocks that read a State
/// re-run when it changes.
///
/// Press SPACE to flip the colour.
/// </summary>
public partial class State_Example : SpokeNode2D {

    // A reactive variable. Changing it notifies everything that read it.
    State<bool> isRed = State.Create(false);

    protected override void Init(EffectBuilder s) {

        // Children are added the ordinary Godot way; s.OnCleanup is the teardown half.
        var swatch = new ColorRect {
            Position = new Vector2(40, 80),
            Size = new Vector2(200, 200)
        };
        AddChild(swatch);
        s.OnCleanup(() => swatch.QueueFree());

        var label = new Label {
            Position = new Vector2(40, 40),
            Text = "SPACE  toggle colour"
        };
        AddChild(label);
        s.OnCleanup(() => label.QueueFree());

        // Re-runs whenever isRed changes.
        // s.D(...) reads a signal *and* registers it as a dependency of this block.
        s.Effect(s => {
            swatch.Color = s.D(isRed) ? Colors.Red : Colors.Blue;
        });

        // The same thing with an explicit dependency instead of s.D(...):
        //
        //     s.Effect(s => {
        //         swatch.Color = isRed.Now ? Colors.Red : Colors.Blue;
        //     }, isRed);
        //
        // s.D(...) is generally preferred — the dependency can't drift out of sync with the code.
    }

    public override void _UnhandledKeyInput(InputEvent @event) {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space }) {
            isRed.Update(v => !v);
            // Or: isRed.Set(!isRed.Now);
        }
    }
}
