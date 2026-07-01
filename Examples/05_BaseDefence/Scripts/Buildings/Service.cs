using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Owner for the service world. Every building's Service puts a Receiver collider (its footprint in
    // the network — how providers find it) in the world, and while it provides coverage a Provider
    // collider (its range). Queries filter on IsProvider to tell the two apart.
    public class ServiceBody {
        public readonly Service Service;
        public readonly bool IsProvider;
        public ServiceBody(Service service, bool isProvider) { Service = service; IsProvider = isProvider; }
    }

    public class Service : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Building building;

        [Header("Attributes")]
        [SerializeField] bool isRoot = false;
        [SerializeField] UState<float> receiveRange = new(0.1f);
        [SerializeField] UState<float> provideRange = new(4f);

        State<Service> parent { get; } = new();

        State<bool> hasService = new(false);
        public ISignal<bool> HasService => hasService;

        public bool ProvidesService => provideRange.Now > 0;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(WatchHasService);

                s.Phase(building.Health.IsAlive, s => {
                    // Receiver: my footprint in the service world — always present while alive.
                    var receiver = s.Use(GameState.ServiceZone.AddCollider(new ServiceBody(this, false), () => new Circle(transform.position, building.Radius)));

                    s.Effect(WatchParent(receiver));

                    // Provider: my coverage range, only while powered and only if I provide service.
                    if (!ProvidesService) return;
                    s.Phase(hasService, s => {
                        var provider = s.Use(GameState.ServiceZone.AddCollider(new ServiceBody(this, true), () => new Circle(transform.position, provideRange.Now)));

                        var isRootConnected = s.Memo(IsRootConnected);
                        s.Phase(isRootConnected, PropagateConnections(provider));
                    });
                });
            });
        }

        EffectBlock WatchHasService => s => {
            const float powerDelay = 0.15f;
            var nextHasService = s.Memo(s => isRoot || s.D(parent) != null);
            var shouldChange = s.Memo(s => s.D(nextHasService) != s.D(hasService));
            s.Phase(shouldChange, s => {
                IEnumerator settle() {
                    yield return new WaitForSeconds(powerDelay);
                    hasService.Set(nextHasService.Now);
                }
                var routine = StartCoroutine(settle());
                s.OnCleanup(() => StopCoroutine(routine));
            });
            s.OnCleanup(() => hasService.Set(false));
        };

        EffectBlock WatchParent(ICollider<ServiceBody> receiver) => s => {
            var parentNow = s.D(parent);
            if (parentNow == null) return;
            s.Effect(s => {
                foreach (var c in receiver.Overlaps)
                    if (c.Owner.IsProvider && c.Owner.Service == parentNow) return;
                parent.Set(null);
            }, receiver.OverlapsChanged);
            s.Effect(s => {
                if (!s.D(parentNow.HasService)) parent.Set(null);
            });
            s.OnCleanup(() => parent.Set(null));
        };

        // Connected iff walking my parent chain reaches a root.
        MemoBlock<bool> IsRootConnected => s => {
            var node = this;
            while (node != null) {
                if (node.isRoot) return true;
                node = s.D(node.parent);
            }
            return false;
        };

        EffectBlock PropagateConnections(ICollider<ServiceBody> provider) => s => {
            s.Effect(s => {
                foreach (var c in provider.Overlaps) {
                    if (c.Owner.IsProvider) continue;
                    var svc = c.Owner.Service;
                    if (svc == this || svc.isRoot) continue;
                    var canConnect = s.Memo(s => {
                        var parentNow = s.D(svc.parent);
                        return parentNow == null || parentNow == this;
                    });
                    s.Phase(canConnect, s => svc.parent.Set(this));
                }
            }, provider.OverlapsChanged);
        };

        void OnDrawGizmosSelected() {
            if (!isRoot) new Circle(transform.position, receiveRange.Now).DrawGizmo(Color.magenta);
            if (ProvidesService) new Circle(transform.position, provideRange.Now).DrawGizmo(Color.red);
        }
    }
}
