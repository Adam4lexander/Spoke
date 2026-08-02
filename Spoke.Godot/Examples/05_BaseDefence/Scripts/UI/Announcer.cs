using Godot;

namespace Spoke.Examples.BaseDefence;

// Announces game events over the board: flash messages in the onscreen text,
// and a blinking bar along the screen edge the next wave will attack from.
public partial class Announcer : SpokeControl {

    [ExportGroup("References")]
    [Export] public Label OnscreenText { get; set; }
    [Export] public Control NorthWarning { get; set; }
    [Export] public Control EastWarning { get; set; }
    [Export] public Control SouthWarning { get; set; }
    [Export] public Control WestWarning { get; set; }

    [ExportGroup("Attributes")]
    [Export] public float WaveWarningBlinkTime { get; set; } = 0.5f;   // seconds per on/off phase
    [Export] public float OnscreenMessageTime { get; set; } = 4f;      // seconds a message lingers

    protected override void Init(EffectBuilder s) {
        OnscreenText.Text = "";
        NorthWarning.Visible = false;
        EastWarning.Visible = false;
        SouthWarning.Visible = false;
        WestWarning.Visible = false;

        var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);

        s.Phase(isPlaying, s => {
            s.Effect(ShowWaveWarning);

            // Each announcement is docked under one key, so a new one replaces the one before it.
            var dock = s.Dock();
            s.Subscribe(GameState.Director.WaveStarted, wave =>
                dock.Effect("announce", FlashMessage($"Wave {wave.Number} Incoming\nHarvesters Paused")));
            s.Subscribe(GameState.Director.WaveDefeated, wave =>
                dock.Effect("announce", FlashMessage($"Wave {wave.Number} Defeated")));
        });
    }

    // Shows a message in the onscreen text, clearing it after a few seconds.
    EffectBlock FlashMessage(string message) => s => {
        OnscreenText.Text = message;
        s.OnCleanup(() => OnscreenText.Text = "");
        s.Wait(OnscreenMessageTime, () => OnscreenText.Text = "");
    };

    // Blink along the threatened screen edge once the wave's direction is revealed.
    EffectBlock ShowWaveWarning => s => {
        var waveFront = s.Memo(s => {
            var wave = s.D(GameState.Director.Wave);
            return wave.IsAssaulting ? WaveFront.None : wave.Front;
        });

        s.Effect(s => {
            var bar = s.D(waveFront) switch {
                WaveFront.North => NorthWarning,
                WaveFront.East => EastWarning,
                WaveFront.South => SouthWarning,
                WaveFront.West => WestWarning,
                _ => null,
            };
            if (bar == null) return;

            bar.Visible = true;
            s.OnCleanup(() => bar.Visible = false);

            s.Every(WaveWarningBlinkTime, () => bar.Visible = !bar.Visible);
        });
    };
}
