using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class BombBlast : SpokeBehaviour {

        [Header("References")]
        [SerializeField] ParticleSystem fxRoot;

        [Header("Attributes")]
        [SerializeField] float radius;
        [SerializeField] float damage;
        [SerializeField] float duration;

        protected override void Init(EffectBuilder s) {
            fxRoot.transform.localScale = new Vector3(radius, 1f, radius);

            s.Phase(IsEnabled, s => {
                fxRoot.Play(true);

                IEnumerator onUpdate() {
                    yield return new WaitForSeconds(duration);
                    var buildingColliders = GameState.BuildingZone.Query(new Circle(transform.position, radius));
                    foreach (var collider in buildingColliders) {
                        var building = collider.Owner;
                        if (building == null) continue;
                        building.Health.Damage(damage);
                    }
                    Pool.Despawn(gameObject);
                }
                var routine = StartCoroutine(onUpdate());
                s.OnCleanup(() => {
                    StopCoroutine(routine);
                    fxRoot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                });
            });
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}