using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // A delayed area explosion: after a short fuse, damages every building in its radius, then despawns.
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
                    foreach (var collider in GameState.GroundZone.Query(new Circle(transform.position, radius))) {
                        var health = collider.Owner.GetComponent<Health>();
                        if (health == null) continue;
                        health.Damage(damage);
                    }
                    Pool.Despawn(gameObject);
                }
                s.Coroutine(onUpdate());
                s.OnCleanup(() => fxRoot.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear));
            });
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}