using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Service : SpokeBehaviour {

        static Trigger resettle = Trigger.Create();

        [Header("References")]
        [SerializeField] Building building;

        [Header("Attributes")]
        [SerializeField] float range = 5f;

        State<int> hop = new(int.MaxValue);
        public ISignal<int> Hop => hop;

        protected override void Init(EffectBuilder s) {

            if (!building.IsCore) {
                s.Subscribe(resettle, () => hop.Set(int.MaxValue));
            }

            s.Phase(IsEnabled, s => {

                s.OnCleanup(() => {
                    if (hop.Now != int.MaxValue) resettle.Invoke();
                });

                s.Effect(Propagate, resettle);

                var reachable = s.Memo(s => s.D(hop) != int.MaxValue);
                s.Phase(reachable, s => {
                    var zone = GameState.Instance.ServiceZone;
                    s.Use(zone.Add(this, new Circle(transform.position, range)));
                });
            });
        }

        EffectBlock Propagate => s => {
            var zone = GameState.Instance.ServiceZone;

            if (building.IsCore) {
                hop.Set(0);
                return;
            }

            var watch = s.Use(zone.Watch(new Circle(transform.position, 0f)));

            s.Effect(s => {
                var nearest = int.MaxValue;
                foreach (var entry in s.D(watch.Items)) {
                    if (ReferenceEquals(entry.RefObject, this)) continue;
                    nearest = Mathf.Min(nearest, s.D(entry.RefObject.Hop));
                }
                hop.Set(nearest == int.MaxValue ? int.MaxValue : nearest + 1);
            });
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.red);
        }
    }
}
