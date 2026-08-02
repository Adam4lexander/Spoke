using System;
using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

// One entry in the power zone (GameState.PowerZone): a node, tagged as either a provider
// (coverage range) or a receiver (footprint). Queries filter on IsProvider to tell the two apart.
public class PowerBody {
    public readonly PowerNode Node;
    public readonly bool IsProvider;
    public PowerBody(PowerNode node, bool isProvider) { Node = node; IsProvider = isProvider; }
}

// A building's link to the power grid. Nodes form a tree rooted at the Core: each draws power
// from a parent provider whose coverage it overlaps, and is powered only if that chain reaches
// the root.
public partial class PowerNode : SpokeNode {

    [Export] public Unit Unit { get; set; }

    const float PowerSettleDelay = 0.15f;

    static readonly State<ReadOnlyList<PowerNode>> all = new(new ReadOnlyList<PowerNode>(new List<PowerNode>()));

    /// <summary>Every power node currently in the scene.</summary>
    public static ISignal<ReadOnlyList<PowerNode>> All => all;

    // Publishes a fresh list each change. The wrapper compares by inner-list
    // reference, and State dedups equal values.
    static void UpdateAll(Action<List<PowerNode>> mutate) {
        var next = new List<PowerNode>();
        foreach (var node in all.Now) next.Add(node);
        mutate(next);
        all.Set(new ReadOnlyList<PowerNode>(next));
    }

    [Export] public bool IsRoot { get; set; }
    [Export] public float ReceiveRange { get; set; } = 0.1f;
    [Export] public float ProvideRange { get; set; }

    readonly State<bool> enabled = State.Create(true);
    readonly State<PowerNode> parent = State.Create<PowerNode>(null);
    readonly State<bool> hasPower = State.Create(false);

    /// <summary>Switched off while the unit is dying, the way Unity disables the component.</summary>
    public IState<bool> Enabled => enabled;

    /// <summary>The provider this node draws power from; null for the root or an unpowered node.</summary>
    public ISignal<PowerNode> Parent => parent;

    /// <summary>Whether this node is currently powered.</summary>
    public ISignal<bool> HasPower => hasPower;

    /// <summary>A leaf only draws power; it never relays it onward to other nodes.</summary>
    public bool IsLeaf => ProvideRange <= 0f;

    protected override void Init(EffectBuilder s) {
        var isOnline = s.Memo(s => s.D(IsInTree) && s.D(enabled));

        s.Phase(isOnline, s => {
            UpdateAll(list => list.Add(this));
            s.OnCleanup(() => UpdateAll(list => list.Remove(this)));

            s.Effect(SettleHasPower);
            if (!IsRoot) s.Effect(ReceivePower);
            if (!IsLeaf) s.Phase(hasPower, ProvidePower);
        });
    }

    EffectBlock SettleHasPower => s => {
        if (IsRoot) hasPower.Set(true);

        var nextHasPower = s.Memo(s => IsRoot || s.D(parent) != null);
        var shouldChange = s.Memo(s => s.D(nextHasPower) != s.D(hasPower));

        // If the pending change reverses before the delay elapses, the phase unmounts and takes
        // the timer with it. Nothing has to cancel anything.
        s.Phase(shouldChange, s => s.Wait(PowerSettleDelay, () => hasPower.Set(nextHasPower.Now)));

        s.OnCleanup(() => hasPower.Set(false));
    };

    EffectBlock ReceivePower => s => {
        var collider = s.Use(GameState.PowerZone.AddCollider(
            new PowerBody(this, false),
            () => new Circle(Unit.GlobalPosition, World.Px(ReceiveRange)),
            body => body.IsProvider));

        s.OnCleanup(() => parent.Set(null));

        s.Effect(s => {
            var parentNow = s.D(parent);
            if (parentNow == null) return;

            s.Effect(s => {
                foreach (var c in collider.Overlaps) {
                    if (c.Owner.Node == parentNow) return;
                }
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
            () => new Circle(Unit.GlobalPosition, World.Px(ProvideRange)),
            body => !body.IsProvider));

        // One walk up the chain answers both questions: who my ancestors are
        // (for the steal guard) and whether the chain reaches a root.
        var chain = s.Memo(s => {
            var ancestors = new HashSet<PowerNode>();
            var isRootConnected = false;
            for (var n = this; n != null; n = s.D(n.parent)) {
                if (n != this) ancestors.Add(n);
                isRootConnected |= n.IsRoot;
            }
            return (ancestors, isRootConnected);
        });

        var isRootConnected = s.Memo(s => s.D(chain).isRootConnected);

        s.Phase(isRootConnected, s => {
            foreach (var c in collider.Overlaps) {
                var node = c.Owner.Node;
                if (node == this || node.IsRoot) continue;

                var canConnect = s.Memo(s => {
                    var parentNow = s.D(node.parent);
                    if (parentNow == null || parentNow == this) return true;
                    // Steal the node from a farther provider, unless it's an
                    // ancestor of this one, which would loop the chain.
                    var mine = node.Unit.GlobalPosition.DistanceSquaredTo(Unit.GlobalPosition);
                    var theirs = node.Unit.GlobalPosition.DistanceSquaredTo(parentNow.Unit.GlobalPosition);
                    if (mine >= theirs) return false;
                    return !s.D(chain).ancestors.Contains(node);
                });

                s.Phase(canConnect, s => node.parent.Set(this));
            }
        }, collider.OverlapsChanged);
    };
}
