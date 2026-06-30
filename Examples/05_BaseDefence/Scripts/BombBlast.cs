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

        float timer;
        State<bool> isDone = new(false);

        protected override void Init(EffectBuilder s) {
            fxRoot.transform.localScale = new Vector3(radius, 1f, radius);
            fxRoot.Play(true);

            s.Phase(isDone, s => {
                var buildingColliders = GameState.BuildingZone.Query(new Circle(transform.position, radius));
                foreach (var collider in buildingColliders) {
                    var building = collider.Owner;
                    if (building == null) continue;
                    building.Health.Damage(damage);
                }
                Destroy(gameObject);
            });
        }

        void Update() {
            timer += Time.deltaTime;
            isDone.Set(timer >= duration);
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}