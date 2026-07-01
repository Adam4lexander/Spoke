using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Radar : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Building building;
        [SerializeField] GameObject dishPivot;

        [Header("Attributes")]
        [SerializeField] UState<float> range = new(5f);
        [SerializeField] float dishRotationSpeed;

        protected override void Init(EffectBuilder s) {
            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.Power.HasPower));

            s.Phase(isRunning, s => {
                s.Effect(DishAnimation);
                s.Use(GameState.RadarZone.AddCollider(this, () => new Circle(transform.position, range.Now)));
            });
        }

        EffectBlock DishAnimation => s => {
            IEnumerator onUpdate() {
                while (true) {
                    dishPivot.transform.Rotate(Vector3.up, dishRotationSpeed * Time.deltaTime);
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range.Now);
            circle.DrawGizmo(Color.red);
        }
    }
}