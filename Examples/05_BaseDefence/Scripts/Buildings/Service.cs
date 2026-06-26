using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Service : SpokeBehaviour {

        static Trigger resettle = Trigger.Create();

        [Header("References")]
        [SerializeField] Building building;

        [Header("Attributes")]
        [SerializeField] UState<float> range = new(5f);

        State<int> hop = new(int.MaxValue);

        protected override void Init(EffectBuilder s) {
            if (!building.IsCore) {
                s.Subscribe(resettle, () => hop.Set(int.MaxValue));
            }

            s.Phase(IsEnabled, s => {
                var positionNow = s.D(building.Position);
                var rangeNow = s.D(range);

                s.OnCleanup(() => {
                    if (hop.Now != int.MaxValue) resettle.Invoke();
                });
                s.Effect(Propagate(positionNow), resettle);

                var reachable = s.Memo(s => s.D(hop) != int.MaxValue);
                s.Phase(reachable, s => {
                    s.Use(GameState.Instance.ServiceZone.Add(this, new Circle(positionNow, rangeNow)));
                });
            });
        }

        EffectBlock Propagate(Vector3 position) => s => {
            if (building.IsCore) {
                hop.Set(0);
                return;
            }

            var watch = s.Use(GameState.Instance.ServiceZone.Watch(new Circle(position, 0f)));

            s.Effect(s => {
                var nearest = int.MaxValue;
                foreach (var entry in s.D(watch.Items)) {
                    if (ReferenceEquals(entry.RefObject, this)) continue;
                    nearest = Mathf.Min(nearest, s.D(entry.RefObject.hop));
                }
                hop.Set(nearest == int.MaxValue ? int.MaxValue : nearest + 1);
            });
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range.Now);
            circle.DrawGizmo(Color.red);
        }
    }
}
