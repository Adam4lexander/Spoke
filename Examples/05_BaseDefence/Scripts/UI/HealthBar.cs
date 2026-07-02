using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    [ExecuteAlways]
    public class HealthBar : SpokeBehaviour {

        [Header("References")]
        [SerializeField] UState<Image> barImage = new();

        [Header("Attributes")]
        [SerializeField] UState<Color> healthyColour = new(Color.green);
        [SerializeField] UState<Color> moderateColour = new(Color.yellow);
        [SerializeField] UState<Color> severeColour = new (Color.red);

        [Header("Inputs")]
        [SerializeField] UState<float> fraction = new(1f);

        public IState<float> Fraction => fraction;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                if (s.D(barImage) == null) return;
                if (s.D(UnitySignals.IsPlaying)) s.Effect(FaceCamera);
                s.Effect(SyncFraction);
            });
        }

        EffectBlock FaceCamera => s => {
            var view = s.D(GameState.View);
            var viewForwards = view.Rotation * Vector3.forward;
            var viewUp = view.Rotation * Vector3.up;

            transform.rotation = Quaternion.LookRotation(-viewForwards, viewUp);
        };

        EffectBlock SyncFraction => s => {
            var clampedFrac = s.Memo(s => Mathf.Clamp01(s.D(fraction)));

            var colour = s.Memo(s => {
                var hpFracNow = s.D(clampedFrac);
                if (hpFracNow > 0.7f) return s.D(healthyColour);
                if (hpFracNow > 0.3) return s.D(moderateColour);
                return s.D(severeColour);
            });

            s.Effect(s => {
                var barImageNow = s.D(barImage);
                // Images pivot point is on the left-hand side. So scale on x-axis
                barImageNow.rectTransform.localScale = new Vector3(s.D(clampedFrac), 1f, 1f);
                barImageNow.color = s.D(colour);
            });
        };
    }
}