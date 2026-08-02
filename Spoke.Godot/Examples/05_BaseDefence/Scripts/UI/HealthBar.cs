using Godot;

namespace Spoke.Examples.BaseDefence;

// A fractional bar that shrinks and recolours (healthy/moderate/severe) with its Fraction. There's
// no billboarding to do in 2D, so the whole job is one signal in, a colour and a width out.
public partial class HealthBar : SpokeNode2D {

    [Export] public float Width { get; set; } = 0.7f;
    [Export] public float Height { get; set; } = 0.1f;

    readonly State<float> _fraction = State.Create(1f);
    readonly State<Color> colour = State.Create(Palette.Healthy);

    // Exporting a property over the state puts it in the Inspector, so the bar can be
    // scrubbed in the editor as well as driven from code.
    [ExportGroup("Inputs")]
    [Export] float fraction { get => _fraction.Now; set => _fraction.Set(value); }

    /// <summary>The fill amount, 0 to 1, driving the bar's size and colour.</summary>
    public IState<float> Fraction => _fraction;

    protected override void Init(EffectBuilder s) {
        ZIndex = 5;

        var clamped = s.Memo(s => Mathf.Clamp(s.D(_fraction), 0f, 1f));

        s.Effect(s => {
            var f = s.D(clamped);
            colour.Set(f > 0.7f ? Palette.Healthy : f > 0.3f ? Palette.Moderate : Palette.Severe);
        });

        // Repaint only when a value the bar shows actually changed.
        s.Effect(s => {
            s.D(clamped);
            s.D(colour);
            QueueRedraw();
        });
    }

    public override void _Draw() {
        var w = World.Px(Width);
        var h = World.Px(Height);
        var origin = new Vector2(-w * 0.5f, -h * 0.5f);
        DrawRect(new Rect2(origin, new Vector2(w, h)), Palette.BarBacking);
        DrawRect(new Rect2(origin, new Vector2(w * Mathf.Clamp(_fraction.Now, 0f, 1f), h)), colour.Now);
    }
}
