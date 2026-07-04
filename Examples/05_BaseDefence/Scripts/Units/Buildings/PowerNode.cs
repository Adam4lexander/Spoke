using System;
using System.Collections;
using System.Collections.Generic;
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

        static readonly State<ReadOnlyList<PowerNode>> all = new(new ReadOnlyList<PowerNode>(new List<PowerNode>()));
        public static ISignal<ReadOnlyList<PowerNode>> All => all;

        // Publishes a fresh list each change — the wrapper compares by inner-list
        // reference, and State dedups equal values.
        static void UpdateAll(Action<List<PowerNode>> mutate) {
            var next = new List<PowerNode>();
            foreach (var node in all.Now) next.Add(node);
            mutate(next);
            all.Set(new ReadOnlyList<PowerNode>(next));
        }

        [Header("Attributes")]
        [SerializeField] bool isRoot = false;
        [SerializeField] UState<float> receiveRange = new(0.1f);
        [SerializeField] UState<float> provideRange = new(4f);

        State<PowerNode> parent = new();
        public ISignal<PowerNode> Parent => parent;

        State<bool> hasPower = new(false);
        public ISignal<bool> HasPower => hasPower;

        // A leaf only draws power; it never relays it onward to other nodes.
        public bool IsLeaf => provideRange.Now <= 0;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                UpdateAll(list => list.Add(this));
                s.OnCleanup(() => UpdateAll(list => list.Remove(this)));

                s.Effect(DebounceHasPowerChange);
                if (!isRoot) s.Effect(ReceivePower);
                if (!IsLeaf) s.Phase(hasPower, ProvidePower);
            });
        }

        EffectBlock DebounceHasPowerChange => s => {
            const float powerDelay = 0.15f;
            if (isRoot) hasPower.Set(true);
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

        EffectBlock ReceivePower => s => {
            var collider = s.Use(GameState.PowerZone.AddCollider(
                new PowerBody(this, false),
                () => new Circle(transform.position, receiveRange.Now),
                body => body.IsProvider));

            s.OnCleanup(() => parent.Set(null));

            s.Effect(s => {
                var parentNow = s.D(parent);
                if (parentNow == null) return;
                s.Effect(s => {
                    foreach (var c in collider.Overlaps)
                        if (c.Owner.Node == parentNow) return;
                    parent.Set(null);
                }, collider.OverlapsChanged);
                s.Effect(s => {
                    if (!s.D(parentNow.HasPower)) parent.Set(null);
                });
            });
        };

        EffectBlock ProvidePower => s => {
            var collider = s.Use(GameState.PowerZone.AddCollider(
                new PowerBody(this, true), 
                () => new Circle(transform.position, provideRange.Now), 
                body => !body.IsProvider));

            // One walk up the chain answers both questions: who my ancestors are
            // (for the steal guard) and whether the chain reaches a root.
            var chain = s.Memo(s => {
                var ancestors = new HashSet<PowerNode>();
                var isRootConnected = false;
                for (var n = this; n != null; n = s.D(n.parent)) {
                    if (n != this) ancestors.Add(n);
                    isRootConnected |= n.isRoot;
                }
                return (ancestors, isRootConnected);
            });

            var isRootConnected = s.Memo(s => s.D(chain).isRootConnected);
            s.Phase(isRootConnected, s => {
                foreach (var c in collider.Overlaps) {
                    var node = c.Owner.Node;
                    if (node == this || node.isRoot) continue;
                    var canConnect = s.Memo(s => {
                        var parentNow = s.D(node.parent);
                        if (parentNow == null || parentNow == this) return true;
                        // Steal the node from a farther provider — unless it's an
                        // ancestor of this one, which would loop the chain.
                        var mine = (node.transform.position - transform.position).sqrMagnitude;
                        var theirs = (node.transform.position - parentNow.transform.position).sqrMagnitude;
                        if (mine >= theirs) return false;
                        return !s.D(chain).ancestors.Contains(node);
                    });
                    s.Phase(canConnect, s => node.parent.Set(this));
                }
            }, collider.OverlapsChanged);
        };

        void OnDrawGizmosSelected() {
            if (!isRoot) new Circle(transform.position, receiveRange.Now).DrawGizmo(Color.magenta);
            if (!IsLeaf) new Circle(transform.position, provideRange.Now).DrawGizmo(Color.red);
        }
    }
}
