using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Enemy : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject fireFrom;

        [Header("Attributes")]
        [SerializeField] float radius;

        State<bool> isTracked = new(false);
        public ISignal<bool> IsTracked => isTracked;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                var position = s.Effect(WatchPosition);
                s.Effect(WatchIsTracked(position));
                s.Phase(isTracked, s => {
                    s.Use(GameState.Instance.TrackedEnemyZone.Add(this, new Circle(s.D(position), radius)));
                });
            });
        }

        EffectBlock<Vector3> WatchPosition => s => {
            var position = State.Create(transform.position);
            IEnumerator onUpdate() {
                while (true) {
                    position.Set(transform.position);
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
            return position;
        };

        EffectBlock WatchIsTracked(ISignal<Vector3> position) => s => {
            var watch = s.Use(GameState.Instance.RadarZone.Watch(new Circle(s.D(position), radius)));
            s.Effect(s => {
                isTracked.Set(s.D(watch.Items).Count > 0);
            });
            s.OnCleanup(() => isTracked.Set(false));
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}