using System;

namespace Spoke {

    /// <summary>
    /// The per-frame callback attached by s.OnProcess and s.OnPhysicsProcess.
    /// It runs while the block that declared it is mounted, and its place in the Spoke tree is what
    /// orders it against the node's other callbacks.
    /// </summary>
    public sealed class FrameTick : Epoch {

        readonly Action<double> fn;
        readonly bool isPhysics;

        public FrameTick(string name, Action<double> fn, bool isPhysics) {
            Name = name;
            this.fn = fn ?? throw new ArgumentNullException(nameof(fn));
            this.isPhysics = isPhysics;
        }

        // The node drives the callback, so there's nothing here for Spoke to run.
        protected override bool AutoArmTickAfterInit => false;

        protected override TickBlock Init(EpochBuilder s) {
            if (!s.TryImport<GodotContext>(out var ctx) || ctx.Core == null) {
                throw new InvalidOperationException(
                    "s.OnProcess needs a tree hosted by a node — SpokeNode, SpokeNode2D, SpokeNode3D, " +
                    "SpokeControl, or SpokeHost.Attach. A hand-spawned tree has no frame to run on.");
            }
            s.Use(ctx.Core.Register(this, fn, isPhysics));
            return null;
        }
    }
}
