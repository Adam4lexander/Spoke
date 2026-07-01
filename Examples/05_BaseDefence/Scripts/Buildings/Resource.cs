using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Resource : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject harvestEffect;

        [Header("Attributes")]
        [SerializeField] float radius;

        protected override void Init(EffectBuilder s) {
            harvestEffect.SetActive(false);
            s.Phase(IsEnabled, s => {
                s.Effect(Harvest);
            });
        }

        EffectBlock Harvest => s => {
            var sensor = s.Use(GameState.PowerZone.AddSensor(() => new Circle(transform.position, radius)));

            var isHarvested = s.Memo(s => {
                foreach (var body in sensor.Overlaps) {
                    if (body.Owner.IsProvider) return true;
                }
                return false;
            }, sensor.OverlapsChanged);

            s.Phase(isHarvested, s => {
                harvestEffect.SetActive(true);
                s.OnCleanup(() => harvestEffect.SetActive(false));
            });
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}