using System;
using System.Collections.Generic;
using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// One entry in the power zone: a node, tagged as either a provider (coverage range) or a receiver
/// (footprint). Queries filter on IsProvider to tell the two apart.
/// </summary>
public class PowerBody {
    public readonly PowerNode Node;
    public readonly bool IsProvider;
    public PowerBody(PowerNode node, bool isProvider) { Node = node; IsProvider = isProvider; }
}

/// <summary>
/// A unit's link to the power grid, and a child of the unit's scene. Nodes form a tree rooted at
/// the Core: each draws power from a parent provider whose coverage it overlaps, and is powered
/// only if that chain reaches the root.
///
/// This is the piece of the game that most repays being reactive. Nothing here polls, and nothing
/// walks the grid on a timer. Destroying a relay drops one node's parent, and the loss propagates
/// out through the branch below it because each node's HasPower reads its parent's.
///
/// Ranges are in metres, matching the Unity prefabs.
/// </summary>
public partial class PowerNode : SpokeNode2D {

    const float PowerSettleDelay = 0.15f;

    static readonly State<ReadOnlyList<PowerNode>> all = new(new ReadOnlyList<PowerNode>(new List<PowerNode>()));

    /// <summary>Every power node currently on the board.</summary>
    public static ISignal<ReadOnlyList<PowerNode>> All => all;

    // Publishes a fresh list on each change. State dedups by the wrapper's inner-list reference.
    static void UpdateAll(Action<List<PowerNode>> mutate) {
        var next = new List<PowerNode>();
        foreach (var node in all.Now) next.Add(node);
        mutate(next);
        all.Set(new ReadOnlyList<PowerNode>(next));
    }

    /// <summary>The Core, and only the Core. A root is powered by definition and has no parent.</summary>
    [Export] public bool IsRoot { get; set; }

    /// <summary>How close a provider's coverage must come to count as reaching this node, in metres.</summary>
    [Export] public float ReceiveRange { get; set; } = 0.1f;

    /// <summary>How far this node relays power onward, in metres. Zero for a leaf.</summary>
    [Export] public float ProvideRange { get; set; }

    readonly State<PowerNode> parent = State.Create<PowerNode>(null);
    readonly State<bool> hasPower = State.Create(false);

    /// <summary>The provider this node draws power from; null for the root, or an unpowered node.</summary>
    public ISignal<PowerNode> Parent => parent;

    /// <summary>Whether this node is currently powered.</summary>
    public ISignal<bool> HasPower => hasPower;

    /// <summary>A leaf only draws power; it never relays it onward.</summary>
    public bool IsLeaf => ProvideRange <= 0f;

    protected override void Init(EffectBuilder s) {
        // The unit this belongs to. Its Health exists from its constructor, so it's safe to read
        // here even though a child's _Ready runs before its parent's.
        var unit = GetParent<Unit>();

        // IsInTree is this game's IsEnabled. A pooled unit leaves the tree without being destroyed,
        // so everything below unmounts on despawn and mounts fresh on reuse.
        var isOnline = s.Memo(s => s.D(IsInTree) && s.D(unit.Health.IsAlive));

        s.Phase(isOnline, s => {
            UpdateAll(list => list.Add(this));
            s.OnCleanup(() => UpdateAll(list => list.Remove(this)));

            s.Effect(SettleHasPower);
            if (!IsRoot) s.Effect(ReceivePower);
            if (!IsLeaf) s.Phase(hasPower, ProvidePower);
        });
    }

    // Power changes are held for a beat before they take effect, so a grid rearranging itself
    // doesn't strobe every building it touches.
    EffectBlock SettleHasPower => s => {
        if (IsRoot) hasPower.Set(true);

        var nextHasPower = s.Memo(s => IsRoot || s.D(parent) != null);
        var shouldChange = s.Memo(s => s.D(nextHasPower) != s.D(hasPower));

        // If the pending change reverses before the delay elapses, shouldChange goes false, the
        // phase unmounts, and the timer goes with it. Nothing has to cancel anything.
        s.Phase(shouldChange, s => s.Wait(PowerSettleDelay, () => hasPower.Set(nextHasPower.Now)));

        s.OnCleanup(() => hasPower.Set(false));
    };

    EffectBlock ReceivePower => s => {
        var collider = s.Use(GameState.PowerZone.AddCollider(
            new PowerBody(this, false),
            () => new Circle(GlobalPosition, World.Px(ReceiveRange)),
            body => body.IsProvider));

        s.OnCleanup(() => parent.Set(null));

        s.Effect(s => {
            var parentNow = s.D(parent);
            if (parentNow == null) return;

            // Drop the parent if we drift out of its coverage...
            s.Effect(s => {
                foreach (var c in collider.Overlaps) {
                    if (c.Owner.Node == parentNow) return;
                }
                parent.Set(null);
            }, collider.OverlapsChanged);

            // ...or if it loses power itself.
            s.Effect(s => {
                if (!s.D(parentNow.HasPower)) parent.Set(null);
            });
        });
    };

    EffectBlock ProvidePower => s => {
        var collider = s.Use(GameState.PowerZone.AddCollider(
            new PowerBody(this, true),
            () => new Circle(GlobalPosition, World.Px(ProvideRange)),
            body => !body.IsProvider));

        // One walk up the chain answers both questions: who my ancestors are (for the steal guard),
        // and whether the chain reaches a root.
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
                    // Steal the node from a farther provider, unless it's one of our own ancestors,
                    // which would close the chain into a loop.
                    var mine = node.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                    var theirs = node.GlobalPosition.DistanceSquaredTo(parentNow.GlobalPosition);
                    if (mine >= theirs) return false;
                    return !s.D(chain).ancestors.Contains(node);
                });

                s.Phase(canConnect, s => node.parent.Set(this));
            }
        }, collider.OverlapsChanged);
    };
}
