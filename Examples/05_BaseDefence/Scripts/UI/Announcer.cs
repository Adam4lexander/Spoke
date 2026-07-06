using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    // Announces game events over the board: flash messages in the onscreen text,
    // and a blinking bar along the screen edge the next wave will attack from.
    public class Announcer : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Text onscreenText;
        [SerializeField] GameObject northWaveWarning;
        [SerializeField] GameObject eastWaveWarning;
        [SerializeField] GameObject southWaveWarning;
        [SerializeField] GameObject westWaveWarning;

        [Header("Attributes")]
        [SerializeField] float waveWarningBlinkTime = 0.5f;   // seconds per on/off phase
        [SerializeField] float onscreenMessageTime = 4f;      // seconds an onscreen message lingers

        protected override void Init(EffectBuilder s) {
            onscreenText.text = "";
            northWaveWarning.SetActive(false);
            eastWaveWarning.SetActive(false);
            southWaveWarning.SetActive(false);
            westWaveWarning.SetActive(false);

            s.Phase(IsEnabled, s => {
                var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);

                s.Phase(isPlaying, s => {
                    s.Effect(ShowWaveWarning);

                    // Announce each wave transition; the opening lull has nothing to announce.
                    s.Effect(s => {
                        var wave = s.D(GameState.Director.Wave);
                        if (s.D(GameState.Director.IsAssaulting)) s.Effect(FlashMessage($"Wave {wave} Incoming\nHarvesters Paused"));
                        else if (wave > 0) s.Effect(FlashMessage($"Wave {wave} Defeated"));
                    });
                });
            });
        }

        // Shows a message in the onscreen text, clearing it after a few seconds.
        EffectBlock FlashMessage(string message) => s => {
            onscreenText.text = message;
            s.OnCleanup(() => onscreenText.text = "");

            IEnumerator onUpdate() {
                yield return new WaitForSeconds(onscreenMessageTime);
                onscreenText.text = "";
            }
            s.Coroutine(onUpdate());
        };

        // Blink along the threatened screen edge once the wave's direction is revealed.
        EffectBlock ShowWaveWarning => s => {
            if (s.D(GameState.Director.IsAssaulting)) return;
            var bar = s.D(GameState.Director.Front) switch {
                WaveFront.North => northWaveWarning,
                WaveFront.East => eastWaveWarning,
                WaveFront.South => southWaveWarning,
                WaveFront.West => westWaveWarning,
                _ => null,
            };
            if (bar == null) return;

            bar.SetActive(true);
            s.OnCleanup(() => bar.SetActive(false));

            var timer = 0f;
            s.Coroutine(() => {
                timer += Time.unscaledDeltaTime;
                if (timer >= waveWarningBlinkTime) {
                    timer = 0f;
                    bar.SetActive(!bar.activeSelf);
                }
            });
        };
    }
}
