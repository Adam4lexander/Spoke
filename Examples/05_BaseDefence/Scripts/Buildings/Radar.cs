using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Radar : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Building building;
        [SerializeField] GameObject dishPivot;

        [Header("Attributes")]
        [SerializeField] float range;
        [SerializeField] float dishRotationSpeed;

        protected override void Init(EffectBuilder s) {
            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.HasService));

            s.Phase(isRunning, s => {
                s.Effect(DishAnimation);
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
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.red);
        }
    }
}