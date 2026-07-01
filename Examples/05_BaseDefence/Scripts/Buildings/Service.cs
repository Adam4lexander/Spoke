using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Service : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Building building;

        [Header("Attributes")]
        [SerializeField] UState<float> range = new(5f);

        public Building Building => building;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Phase(building.HasService, s => {
                    var coverage = s.Use(GameState.ServiceZone.AddCollider(this, new Circle(building.Position.Now, range.Now)));
                    s.Effect(s => coverage.Circle = new Circle(s.D(building.Position), s.D(range)));

                    var isConnected = s.Memo(WatchIsConnected);
                    s.Phase(isConnected, PropagateConnections);
                });
            });
        }

        MemoBlock<bool> WatchIsConnected => s => {
            var node = this;
            while (node != null) {
                if (node.building.IsCore) return true;
                node = s.D(node.building.Parent);
            }
            return false;
        };

        EffectBlock PropagateConnections => s => {
            var sensor = s.Use(GameState.BuildingZone.AddSensor(new Circle(building.Position.Now, range.Now)));
            s.Effect(s => sensor.Circle = new Circle(s.D(building.Position), s.D(range)));

            s.Effect(s => {
                foreach (var collider in sensor.Overlaps) {
                    var building = collider.Owner;
                    if (building == this.building || building.IsCore) continue;
                    var canConnect = s.Memo(s => {
                        var parentNow = s.D(building.Parent);
                        return parentNow == null || parentNow == this;
                    });
                    s.Phase(canConnect, s => {
                        building.Parent.Set(this);
                    });
                }
            }, sensor.OverlapsChanged);
        };

        void OnDrawGizmosSelected() {
            new Circle(transform.position, range.Now).DrawGizmo(Color.red);
        }
    }
}
