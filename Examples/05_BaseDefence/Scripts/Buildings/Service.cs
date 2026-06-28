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
                // On resettle every non-core service drops to unreachable up front. Clearing
                // synchronously, before anything recomputes, is what breaks stale cycles when an
                // island is severed from the core.
                s.Subscribe(resettle, () => hop.Set(int.MaxValue));
            }

            s.Phase(IsEnabled, s => {
                s.OnCleanup(() => resettle.Invoke());
                s.Reaction(s => resettle.Invoke(), building.Position, range);

                s.Effect(Propagate);

                var reachable = s.Memo(s => s.D(hop) != int.MaxValue);
                s.Phase(reachable, s => {
                    var collider = s.Use(GameState.ServiceZone.Add(this, new Circle(building.Position.Now, range.Now)));
                    s.Effect(s => collider.Circle = new Circle(s.D(building.Position), s.D(range)));
                });
            });
        }

        EffectBlock Propagate => s => {
            if (building.IsCore) {
                hop.Set(0);
                return;
            }
            var sensor = s.Use(GameState.ServiceZone.Add(default, new Circle(building.Position.Now, 0f), detects: true, detectable: false));
            s.Effect(s => sensor.Circle = new Circle(s.D(building.Position), 0f));
            s.Effect(s => {
                var nearest = int.MaxValue;
                foreach (var collider in sensor.Overlaps) {
                    if (ReferenceEquals(collider.Payload, this)) continue;
                    nearest = Mathf.Min(nearest, s.D(collider.Payload.hop));
                }
                hop.Set(nearest == int.MaxValue ? int.MaxValue : nearest + 1);
            }, sensor.Changed, resettle);
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range.Now);
            circle.DrawGizmo(Color.red);
        }
    }
}
