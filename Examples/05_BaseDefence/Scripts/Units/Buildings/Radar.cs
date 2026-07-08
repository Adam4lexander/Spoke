using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Reveals enemies within its coverage so turrets can target them. Runs only while powered.
    public class Radar : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;
        [SerializeField] GameObject dishPivot;

        [Header("Attributes")]
        [SerializeField] float range = 8f;
        [SerializeField] float dishRotationSpeed;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo(
                $"{building.DisplayName.ToUpper()}\n\n" +
                "Reveals enemies inside its coverage to turrets.\n\n" +
                "Turrets cannot fire at enemies that no radar has revealed.",
                CoverageType.Radar, building.Power));

            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.Power.HasPower));

            s.Phase(isRunning, s => {
                s.Use(GameState.RadarZone.AddCollider(this, () => new Circle(transform.position, range)));

                s.Coroutine(() => dishPivot.transform.Rotate(Vector3.up, dishRotationSpeed * Time.deltaTime));
            });
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.red);
        }
    }
}