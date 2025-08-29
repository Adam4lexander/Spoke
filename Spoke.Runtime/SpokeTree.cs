using System;

namespace Spoke {

    public enum FlushMode { Auto, Manual }

    /// <summary>
    /// Root ticker for a Spoke tree.
    /// It hosts the top-most epoch (Main) and serves as the entrypoint for driving ticks into the tree.
    /// In Auto mode, its orchestrated by the Spoke runtime.
    /// In Manual mode, ticks are delivered explicitely via user code.
    /// </summary>
    public sealed class SpokeTree<T> : SpokeTree where T : Epoch {

        // Internal command telling OnTick whether to do a single step or a full drain
        enum CommandKind { None, Tick, Flush }

        CommandKind command;
        TickerPorts ports;

        /// <summary>The top-most user-provided epoch. Think of this as the program's 'main'.</summary>
        public T Main { get; private set; }
        /// <summary>True while the ticker has epochs queued to tick (ordered by tree-coords).</summary>
        public bool HasPending => ports.HasPending;

        /// <summary>
        /// Spawns a tree (manual or auto) and attaches the main epoch under it.        
        /// 
        /// - Any exception during attachment is logged as an uncaught bootstrap error. We don't rethrow here because
        /// tree creation should not crash the host; the tree will be faulted and cease from further ticks.
        /// </summary>
        public SpokeTree(string name, T main, FlushMode flushMode, int flushLayer, params object[] services) {
            Name = name;
            Main = main;
            FlushMode = flushMode;
            FlushLayer = flushLayer;

            if (FlushMode == FlushMode.Manual) {
                (this as Ticker.Friend).SetToManual();  // Stops me requesting ticks from Spoke runtime
            } else { 
                command = CommandKind.Flush;            // Auto trees always have command=Flush
                isPendingEagerTick = true;              // Boosts flush layer priority for initial tick
            }

            // Reflect the spawn on the virtual stack
            (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Bootstrap, this));
            TimeStamp = SpokeRuntime.Local.TimeStamp;   // spawn-time ordering tie-breaker
            try {
                // Call Attach without a parent. This will in turn call Bootstrap()
                (this as Epoch.Friend).Attach(null, default, null, services);
            } catch (Exception e) {
                SpokeError.Log("[SpokeTree] uncaught error in Bootstrap", e);
            } finally {
                (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            }
        }

        // Wire up ticking policy for the SpokeTree
        protected override Epoch Bootstrap(TickerBuilder s) {
            // Check for an ISpokeLogger provided as a service. If none, then use default logger
            if (!s.TryImport(out ISpokeLogger logger)) {
                logger = SpokeError.DefaultLogger;
            }
            ports = s.Ports;

            s.OnTick(s => {
                const long maxPasses = 1000; // Infinite loop guard. Maximum number of oscillations
                if (command == CommandKind.None) {
                    return;
                }
                isPendingEagerTick = false;
                var passCount = 0;
                Epoch prev = null;

                // Drive ticks according to command: single step or full flush
                while (ports.HasPending) {
                    if (passCount > maxPasses) {
                        throw new Exception("Exceed iteration limit - possible infinite loop");
                    }
                    try {
                        // If the next epoch comes before the last one by tree-coord order, we increment passcount
                        var next = ports.PeekNext();
                        if (prev != null && prev.CompareTo(next) > 0) {
                            passCount++;
                        }
                        prev = next;

                        s.TickNext(); // Tick the next pending epoch in tree-coord order
                    } catch (SpokeException se) {
                        // Tree-level fault boundary: record on the tree and log the virtual stack + tree trace.
                        (this as Epoch.Friend).SetFault(se);
                        logger?.Error($"FLUSH ERROR\n->A fault occurred during flush. \n\n{se}");
                        break; // stop this flush; the tree remains faulted
                    }

                    // In single-step mode, leave the rest of the queue for a future tick.
                    if (command == CommandKind.Tick) break;
                }
            });

            // Attaches the user-provided main epoch directly under this ticker.
            return Main;
        }

        /// <summary>
        /// Synchronously drains all pending work for Manual trees.
        /// - Valid only when FlushMode==Manual (auto trees are runtime-driven)
        /// - Throws on re-entrancy, as re-entrant flushes aren't possible
        /// </summary>
        public override void Flush() {
            if (FlushMode != FlushMode.Manual) {
                throw new Exception("Only trees with Manual flush policy can be explicitely flushed");
            }
            if ((this as Epoch.Friend).GetControlHandle().IsAlive) {
                throw new Exception("Re-entrant flush detected");
            }
            command = CommandKind.Flush;
            (SpokeRuntime.Local as SpokeRuntime.Friend).TickTree(this);
            command = CommandKind.None;
        }

        /// <summary>
        /// Synchronously ticks exactly one pending epoch for Manual trees.
        /// Same invariants as <see cref="Flush"/>, but processes a single TickNext().
        /// </summary>
        public override void Tick() {
            if (FlushMode != FlushMode.Manual) {
                throw new Exception("Only trees with Manual flush policy can be explicitely ticked");
            }
            if ((this as Epoch.Friend).GetControlHandle().IsAlive) {
                throw new Exception("Re-entrant flush detected");
            }
            command = CommandKind.Tick;
            (SpokeRuntime.Local as SpokeRuntime.Friend).TickTree(this);
            command = CommandKind.None;
        }
    }

    /// <summary>
    /// Abstract base for the root ticker. 
    /// </summary>
    public abstract class SpokeTree : Ticker, IDisposable, SpokeTree.Friend {
        
        new internal interface Friend { 
            bool IsPendingEagerTick(); 
        }

        // Convenience spawners. See docs: Spawn (default), SpawnEager (higher priority), SpawnManual (user-driven).
        public static SpokeTree<T> Spawn<T>(T root, params object[] services) where T : Epoch 
            => new SpokeTree<T>("SpokeTree", root, FlushMode.Auto, 0, services);

        public static SpokeTree<T> Spawn<T>(string name, T root, params object[] services) where T : Epoch 
            => new SpokeTree<T>(name, root, FlushMode.Auto, 0, services);

        public static SpokeTree<T> SpawnEager<T>(T root, params object[] services) where T : Epoch 
            => new SpokeTree<T>("SpokeTree (Eager)", root, FlushMode.Auto, -1, services);

        public static SpokeTree<T> SpawnEager<T>(string name, T root, params object[] services) where T : Epoch 
            => new SpokeTree<T>(name, root, FlushMode.Auto, -1, services);

        public static SpokeTree<T> SpawnManual<T>(T root, params object[] services) where T : Epoch 
            => new SpokeTree<T>("SpokeTree (Manual)", root, FlushMode.Manual, int.MinValue, services);
            
        public static SpokeTree<T> SpawnManual<T>(string name, T root, params object[] services) where T : Epoch 
            => new SpokeTree<T>(name, root, FlushMode.Manual, int.MinValue, services);

        /// <summary>Flush policy for this tree (Auto or Manual).</summary>
        public FlushMode FlushMode { get; protected set; }
        /// <summary>Priority bucket for scheduler ordering. Lower is higher priority; equal layers do not nest.</summary>
        public int FlushLayer { get; protected set; }
        
        protected long TimeStamp = -1;      // capture of SpokeRuntime.Local.TimeStamp at spawn
        protected bool isPendingEagerTick;  // true exactly once for a newly spawned auto tree

        bool Friend.IsPendingEagerTick() 
            => isPendingEagerTick;

        /// <summary>
        /// Defines ordering among trees when the runtime chooses who flushes next.
        /// Rules:
        /// 1) FlushLayer: lower first (e.g., eager trees at -1 outrank default 0)
        /// 2) Eager bit: newly spawned auto trees are serviced before older pending trees of the same layer
        /// 3) TimeStamp: FIFO among equals
        /// </summary>
        public int CompareTo(SpokeTree other) {
            if (FlushLayer != other.FlushLayer) {
                return FlushLayer.CompareTo(other.FlushLayer);
            }
            if (isPendingEagerTick == other.isPendingEagerTick) {
                return TimeStamp.CompareTo(other.TimeStamp);
            }
            return isPendingEagerTick ? -1 : 1;
        }

        /// <summary>
        /// Disposes the entire tree, but detaching all of its descendants.
        /// If the tree is flushing, disposal is deferred immediately after the flush completes.
        /// </summary>
        public void Dispose() {
            var asFriend = (this as Epoch.Friend);
            asFriend.GetControlHandle().OnPopSelf(asFriend.Detach);
        }
        
        public abstract void Flush();
        
        public abstract void Tick();
    }
}

