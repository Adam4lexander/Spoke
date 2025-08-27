using System;

namespace Spoke {

    public enum FlushMode { Auto, Manual }

    /// <summary>
    /// The SpokeTree is the root ticker of the tree. It lets you instantiate a tree, or dispose it.
    /// </summary>
    public sealed class SpokeTree<T> : SpokeTree where T : Epoch {

        enum CommandKind { None, Tick, Flush }

        CommandKind command;
        TickerPorts ports;

        public T Main { get; private set; }
        public bool HasPending => ports.HasPending;

        public SpokeTree(string name, T main, FlushMode flushMode, int flushLayer, params object[] services) {
            Name = name;
            Main = main;
            FlushMode = flushMode;
            FlushLayer = flushLayer;
            if (FlushMode == FlushMode.Manual) {
                (this as Ticker.Friend).SetToManual();
            } else { 
                command = CommandKind.Flush; 
                isPendingEagerTick = true; 
            }
            (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Bootstrap, this));
            TimeStamp = SpokeRuntime.Local.TimeStamp;
            try {
                (this as Epoch.Friend).Attach(null, default, null, services);
            } catch (Exception e) {
                SpokeError.Log("[SpokeTree] uncaught error in Bootstrap", e);
            } finally {
                (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            }
        }

        protected override Epoch Bootstrap(TickerBuilder s) {
            if (!s.TryImport(out ISpokeLogger logger)) {
                logger = SpokeError.DefaultLogger;
            }
            ports = s.Ports;
            s.OnTick(s => {
                const long maxPasses = 1000;
                if (command == CommandKind.None) {
                    return;
                }
                isPendingEagerTick = false;
                var passCount = 0;
                Epoch prev = null;
                while (ports.HasPending) {
                    if (passCount > maxPasses) {
                        throw new Exception("Exceed iteration limit - possible infinite loop");
                    }
                    try {
                        var next = ports.PeekNext();
                        if (prev != null && prev.CompareTo(next) > 0) {
                            passCount++;
                        }
                        prev = next;
                        s.TickNext();
                    } catch (SpokeException se) {
                        (this as Epoch.Friend).SetFault(se);
                        logger?.Error($"FLUSH ERROR\n->A fault occurred during flush. \n\n{se}");
                        break;
                    }
                    if (command == CommandKind.Tick) break;
                }
            });
            return Main;
        }

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

    public abstract class SpokeTree : Ticker, IDisposable, SpokeTree.Friend {
        
        new internal interface Friend { 
            bool IsPendingEagerTick(); 
        }
        
        public static SpokeTree<T> Spawn<T>(T root, params object[] services) where T : Epoch => new SpokeTree<T>("SpokeTree", root, FlushMode.Auto, 0, services);
        public static SpokeTree<T> Spawn<T>(string name, T root, params object[] services) where T : Epoch => new SpokeTree<T>(name, root, FlushMode.Auto, 0, services);
        public static SpokeTree<T> SpawnEager<T>(T root, params object[] services) where T : Epoch => new SpokeTree<T>("SpokeTree (Default)", root, FlushMode.Auto, -1, services);
        public static SpokeTree<T> SpawnEager<T>(string name, T root, params object[] services) where T : Epoch => new SpokeTree<T>(name, root, FlushMode.Auto, -1, services);
        public static SpokeTree<T> SpawnManual<T>(T root, params object[] services) where T : Epoch => new SpokeTree<T>("SpokeTree (Manual)", root, FlushMode.Manual, int.MinValue, services);
        public static SpokeTree<T> SpawnManual<T>(string name, T root, params object[] services) where T : Epoch => new SpokeTree<T>(name, root, FlushMode.Manual, int.MinValue, services);
        
        public FlushMode FlushMode { get; protected set; }
        public int FlushLayer { get; protected set; }
        
        protected long TimeStamp = -1;
        protected bool isPendingEagerTick;

        bool Friend.IsPendingEagerTick() 
            => isPendingEagerTick;

        public int CompareTo(SpokeTree other) {
            if (FlushLayer != other.FlushLayer) {
                return FlushLayer.CompareTo(other.FlushLayer);
            }
            if (isPendingEagerTick == other.isPendingEagerTick) {
                return TimeStamp.CompareTo(other.TimeStamp);
            }
            return isPendingEagerTick ? -1 : 1;
        }

        public void Dispose() {
            var asFriend = (this as Epoch.Friend);
            asFriend.GetControlHandle().OnPopSelf(asFriend.Detach);
        }
        
        public abstract void Flush();
        
        public abstract void Tick();
    }
}
