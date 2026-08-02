using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// A fractional bar floating above a unit, recolouring healthy → moderate → severe as it drains.
/// A child of the unit's scene, positioned there.
///
/// The Unity version spends most of its length billboarding the bar towards the camera. In 2D
/// there's no billboarding to do, so what's left is the part that was always the interesting bit:
/// one signal in, a colour and a width out.
/// </summary>
public partial class HealthBar : SpokeNode2D {

    /// <summary>Bar width in metres.</summary>
    [Export] public float Width { get; set; } = 0.7f;

    /// <summary>Bar height in metres.</summary>
    [Export] public float Height { get; set; } = 0.1f;

    readonly State<float> _fraction = State.Create(1f);
    readonly State<Color> colour = State.Create(Palette.Healthy);

    // A reactive value reaches the Inspector as a private export over the state. The public member
    // stays the state itself, so the bar can be scrubbed in the editor and driven from code.
    [ExportGroup("Inputs")]
    [Export] float fraction { get => _fraction.Now; set => _fraction.Set(value); }

    /// <summary>The fill amount, 0 to 1, driving the bar's width and colour.</summary>
    public IState<float> Fraction => _fraction;

    protected override void Init(EffectBuilder s) {
        ZIndex = 5;

        var clamped = s.Memo(s => Mathf.Clamp(s.D(_fraction), 0f, 1f));

        s.Effect(s => {
            var f = s.D(clamped);
            colour.Set(f > 0.7f ? Palette.Healthy : f > 0.3f ? Palette.Moderate : Palette.Severe);
        });

        // The bar is redrawn only when a value it shows actually changed — the effect re-runs on a
        // change, and QueueRedraw is the whole of "keep the picture in sync".
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
