using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Service : SpokeBehaviour {

        static ServiceNetwork network = new();

        [Header("References")]
        [SerializeField] Building building;

        [Header("Attributes")]
        [SerializeField] UState<float> range = new(5f);

        State<bool> connected = new(false);
        IBody<Service> sensor;   // range sensor; its overlaps are the services within my range

        // Driven once per frame by GameState to settle connectivity.
        public static void UpdateNetwork() => network.Recompute();

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Phase(building.Health.IsAlive, s => {
                    // My place in the edge graph: a point others can detect, plus a range sensor whose
                    // overlaps are the services within my range.
                    var point = s.Use(GameState.EdgeZone.Add(this, new Circle(building.Position.Now, 0f)));
                    s.Effect(s => point.Circle = new Circle(s.D(building.Position), 0f));
                    sensor = s.Use(GameState.EdgeZone.Add(this, new Circle(building.Position.Now, range.Now), detects: true, detectable: false));
                    s.Effect(s => sensor.Circle = new Circle(s.D(building.Position), s.D(range)));
                    network.Add(this);
                    s.OnCleanup(() => network.Remove(this));
                    // Re-flood when my neighbour set actually changes.
                    s.Subscribe(sensor.Changed, network.Invalidate);
                    s.Phase(connected, s => {
                        var broadcast = s.Use(GameState.ServiceZone.Add(this, new Circle(building.Position.Now, range.Now)));
                        s.Effect(s => broadcast.Circle = new Circle(s.D(building.Position), s.D(range)));
                    });
                });
            });
        }

        void OnDrawGizmosSelected() {
            new Circle(transform.position, range.Now).DrawGizmo(Color.red);
        }

        // Floods connectivity out from the cores through the edge graph: a Service is connected iff
        // the flood reaches it. Each Service owns its edges (via the spatial system); the network
        // just walks them.
        private class ServiceNetwork {

            readonly HashSet<Service> services = new();
            readonly HashSet<Service> reached = new();
            bool dirty;

            public void Add(Service service) { services.Add(service); dirty = true; }
            public void Remove(Service service) { if (services.Remove(service)) dirty = true; }
            public void Invalidate() => dirty = true;

            public void Recompute() {
                if (!dirty) return;
                dirty = false;
                reached.Clear();
                foreach (var service in services)
                    if (service.building.IsCore) Flood(service);
                foreach (var service in services)
                    service.connected.Set(reached.Contains(service));
            }

            void Flood(Service from) {
                if (!reached.Add(from)) return;
                foreach (var neighbour in from.sensor.Overlaps)
                    Flood(neighbour.Payload);
            }
        }
    }
}
