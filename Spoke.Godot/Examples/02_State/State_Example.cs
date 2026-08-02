using Godot;

namespace Spoke.Examples;

public partial class State_Example : SpokeNode2D {

    // State<T> is like a reactive variable you can subscribe to.
    // When its value changes all subscribers will be notified.
    State<bool> isRed = State.Create(false);

    protected override void Init(EffectBuilder s) {

        // Children are added the ordinary Godot way, and s.OnCleanup is the teardown half
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

        // Reactively update the swatch's colour when `isRed` changes.
        s.Effect(s => {
            // s.D(...) tracks a dynamic dependency -- this effect will re-run if `isRed` changes.
            swatch.Color = s.D(isRed) ? Colors.Red : Colors.Blue;
        });

        // You could write the effect with explicit dependencies, instead of using s.D(...)
        /*
        s.Effect(s => {
            swatch.Color = isRed.Now ? Colors.Red : Colors.Blue;
        }, isRed); // Dependency `isRed` given explicitly
        */
        // Generally s.D(...) is preferred though
    }

    public override void _UnhandledKeyInput(InputEvent @event) {
        // Flip the colour each time space is pressed
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space }) {
            isRed.Update(v => !v);
            // Or: isRed.Set(!isRed.Now);
        }
    }
}
