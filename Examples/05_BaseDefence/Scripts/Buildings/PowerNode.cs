using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // Owner for the power world. Every building's PowerNode puts a Receiver collider (its footprint in
    // the network — how providers find it) in the world, and unless it's a leaf a Provider collider
    // (its coverage range). Queries filter on IsProvider to tell the two apart.
    public class PowerBody {
        public readonly PowerNode Node;
        public readonly bool IsProvider;
        public PowerBody(PowerNode node, bool isProvider) { Node = node; IsProvider = isProvider; }
    }

    public class PowerNode : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] bool isRoot = false;
        [SerializeField] UState<float> receiveRange = new(0.1f);
        [SerializeField] UState<float> provideRange = new(4f);

        State<PowerNode> parent { get; } = new();

        State<bool> hasPower = new(false);
        public ISignal<bool> HasPower => hasPower;

        // A leaf only draws power; it never relays it onward to other nodes.
        public bool IsLeaf => provideRange.Now <= 0;

        protected override void Init(EffectBuilder s) {
            // The Building disables this component when it dies, tearing everything below down.
            s.Phase(IsEnabled, s => {
                s.Effect(WatchHasPower);

                // Receiver: my footprint in the power world — how providers find me.
                var receiver = s.Use(GameState.PowerZone.AddCollider(new PowerBody(this, false), () => new Circle(transform.position, receiveRange.Now)));
                s.Effect(WatchParent(receiver));

                // Provider: my coverage range, only while powered and only if I'm not a leaf.
                if (IsLeaf) return;
                s.Phase(hasPower, s => {
                    var provider = s.Use(GameState.PowerZone.AddCollider(new PowerBody(this, true), () => new Circle(transform.position, provideRange.Now)));

                    var isRootConnected = s.Memo(IsRootConnected);
                    s.Phase(isRootConnected, PropagateConnections(provider));
                });
            });
        }

        EffectBlock WatchHasPower => s => {
            const float powerDelay = 0.15f;
            var nextHasPower = s.Memo(s => isRoot || s.D(parent) != null);
            var shouldChange = s.Memo(s => s.D(nextHasPower) != s.D(hasPower));
            s.Phase(shouldChange, s => {
                IEnumerator settle() {
                    yield return new WaitForSeconds(powerDelay);
                    hasPower.Set(nextHasPower.Now);
                }
                var routine = StartCoroutine(settle());
                s.OnCleanup(() => StopCoroutine(routine));
            });
            s.OnCleanup(() => hasPower.Set(false));
        };

        EffectBlock WatchParent(ICollider<PowerBody> receiver) => s => {
            var parentNow = s.D(parent);
            if (parentNow == null) return;
            s.Effect(s => {
                foreach (var c in receiver.Overlaps)
                    if (c.Owner.IsProvider && c.Owner.Node == parentNow) return;
                parent.Set(null);
            }, receiver.OverlapsChanged);
            s.Effect(s => {
                if (!s.D(parentNow.HasPower)) parent.Set(null);
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

        EffectBlock PropagateConnections(ICollider<PowerBody> provider) => s => {
            s.Effect(s => {
                foreach (var c in provider.Overlaps) {
                    if (c.Owner.IsProvider) continue;
                    var node = c.Owner.Node;
                    if (node == this || node.isRoot) continue;
                    var canConnect = s.Memo(s => {
                        var parentNow = s.D(node.parent);
                        return parentNow == null || parentNow == this;
                    });
                    s.Phase(canConnect, s => node.parent.Set(this));
                }
            }, provider.OverlapsChanged);
        };

        void OnDrawGizmosSelected() {
            if (!isRoot) new Circle(transform.position, receiveRange.Now).DrawGizmo(Color.magenta);
            if (!IsLeaf) new Circle(transform.position, provideRange.Now).DrawGizmo(Color.red);
        }
    }
}
