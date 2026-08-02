using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// Announces events over the board: flash messages, and a blinking bar along the screen edge the
/// next wave will attack from.
/// </summary>
public partial class Announcer : SpokeControl {

    // Both serialized on the Announcer in BaseDefence.unity.
    const float BlinkTime = 0.5f;       // seconds per on/off phase
    const float MessageTime = 4f;       // how long a message lingers

    const float BarThickness = 12f;

    protected override void Init(EffectBuilder s) {
        var text = GetNode<Label>("Message");

        var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);

        s.Phase(isPlaying, s => {
            s.Effect(WaveWarning);

            // Each announcement is docked under one key, so a new one replaces the one before it —
            // and replacing it runs its cleanup, which clears the label.
            var dock = s.Dock();
            s.Subscribe(GameState.Director.WaveStarted, wave =>
                dock.Effect("announce", Flash(text, $"Wave {wave.Number} incoming — harvesting paused")));
            s.Subscribe(GameState.Director.WaveDefeated, wave =>
                dock.Effect("announce", Flash(text, $"Wave {wave.Number} defeated")));
        });
    }

    EffectBlock Flash(Label label, string message) => s => {
        label.Text = message;
        s.OnCleanup(() => label.Text = "");
        s.Wait(MessageTime, () => label.Text = "");
    };

    // Blink along the threatened edge once the wave's direction is revealed.
    EffectBlock WaveWarning => s => {
        var front = s.Memo(s => {
            var wave = s.D(GameState.Director.Wave);
            return wave.IsAssaulting ? WaveFront.None : wave.Front;
        });

        s.Effect(s => {
            var side = s.D(front);
            if (side == WaveFront.None) return;

            var bar = s.Own(this, new ColorRect { Color = Palette.WarningBar, MouseFilter = MouseFilterEnum.Ignore });
            Place(bar, side);

            // The blink stops when this block unmounts, which is the moment the front changes or
            // the assault begins.
            var lit = true;
            s.Every(BlinkTime, () => {
                lit = !lit;
                bar.Visible = lit;
            });
        });
    };

    static void Place(Control bar, WaveFront side) {
        switch (side) {
            case WaveFront.West:
                bar.AnchorTop = 0f; bar.AnchorBottom = 1f;
                bar.OffsetLeft = 0f; bar.OffsetRight = BarThickness;
                break;
            case WaveFront.East:
                bar.AnchorLeft = 1f; bar.AnchorRight = 1f;
                bar.AnchorTop = 0f; bar.AnchorBottom = 1f;
                bar.OffsetLeft = -BarThickness; bar.OffsetRight = 0f;
                break;
            case WaveFront.North:
                bar.AnchorRight = 1f;
                bar.OffsetTop = 0f; bar.OffsetBottom = BarThickness;
                break;
            default:
                bar.AnchorRight = 1f;
                bar.AnchorTop = 1f; bar.AnchorBottom = 1f;
                bar.OffsetTop = -BarThickness; bar.OffsetBottom = 0f;
                break;
        }
    }
}
