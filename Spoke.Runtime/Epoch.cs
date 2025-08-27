using System;
using System.Collections.Generic;

namespace Spoke {

    public delegate TickBlock InitBlock(EpochBuilder s);
    public delegate void TickBlock(EpochBuilder s);

    /// <summary>
    /// A declarative, stateful execution unit that lives in the lifecycle tree.
    /// Epochs are invoked declaratively, mounted into nodes, and persist as active objects.
    /// They maintain state, respond to context, expose behaviour, and may spawn child epochs.
    /// </summary>
    public abstract class Epoch : Epoch.Friend, Epoch.Introspect {
        
        internal interface Friend { 
            void Attach(Epoch parent, TreeCoords coords, Ticker ticker, IEnumerable<object> services); 
            void Tick(); 
            void Detach(); 
            SpokeRuntime.Handle GetControlHandle(); 
            Ticker GetTicker(); 
            void SetFault(SpokeException fault); 
        }

        internal interface Introspect { 
            List<Epoch> GetChildren(List<Epoch> storeIn = null); 
            Epoch GetParent(); 
        }

        public TreeCoords Coords { get; private set; }
        public bool IsDetached { get; private set; }
        public SpokeException Fault { get; private set; }

        protected string Name = null;
        protected virtual bool AutoArmTickAfterInit => true;

        List<AttachRecord> attachEvents = new();
        TreeCoords tickCursor;
        Epoch parent;
        int attachIndex;
        Ticker ticker;
        TickBlock tickBlock;
        SpokeRuntime.Handle controlHandle;
        Action _requestTick;
        
        public override string ToString() { 
            return Name ?? GetType().Name;
        }

        public int CompareTo(Epoch other) {
            return tickCursor.CompareTo(other.tickCursor);
        }

        void DetachFrom(int i) {
            while (attachEvents.Count > Math.Max(i, 0)) {
                attachEvents[attachEvents.Count - 1].Detach(this);
                attachEvents.RemoveAt(attachEvents.Count - 1);
            }
        }

        void Friend.Detach() { 
            DetachFrom(0); 
            IsDetached = true; 
        }

        void Friend.Attach(Epoch parent, TreeCoords coords, Ticker ticker, IEnumerable<object> services) {
            this.parent = parent;
            attachIndex = parent != null ? parent.attachEvents.Count - 1 : -1;
            Coords = tickCursor = coords;
            this.ticker = ticker;
            _requestTick = () => {
                if (Fault != null) return;
                if (ticker != null) (ticker as Ticker.Friend)?.Schedule(this);
                else (SpokeRuntime.Local as SpokeRuntime.Friend).Schedule(this);
            };
            Init(services);
        }

        void Init(IEnumerable<object> services) {
            controlHandle = (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Init, this));
            try {
                if (services != null) {
                    foreach (var x in services) {
                        attachEvents.Add(new AttachRecord(AttachRecord.Kind.Export, x));
                    }
                }
                tickBlock = Init(new EpochBuilder(new EpochMutations(this)));
                tickCursor = Coords.Extend(attachEvents.Count);
            } catch (Exception e) {
                if (e is SpokeException se) {
                    if (!se.SkipMarkFaulted) Fault = se;
                    se.SkipMarkFaulted = false;
                    throw;
                }
                throw Fault = new SpokeException("Uncaught Exception in Init", e);
            } finally {
                (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            }
            if (AutoArmTickAfterInit) {
                controlHandle.OnPopSelf(_requestTick);
            }
        }

        protected abstract TickBlock Init(EpochBuilder s);

        void Friend.Tick() {
            if (IsDetached) return;
            controlHandle = (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Tick, this));
            DetachFrom((int)tickCursor.Tail);
            try {
                tickBlock?.Invoke(new EpochBuilder(new EpochMutations(this)));
            } catch (Exception e) {
                if (e is SpokeException se) {
                    if (!se.SkipMarkFaulted) Fault = se;
                    se.SkipMarkFaulted = false;
                    throw;
                }
                throw Fault = new SpokeException("Uncaught Exception in Tick", e);
            } finally {
                (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            }
        }

        SpokeRuntime.Handle Friend.GetControlHandle() { 
            return controlHandle; 
        }

        Ticker Friend.GetTicker() { 
            return ticker; 
        }

        void Friend.SetFault(SpokeException fault) { 
            Fault = fault; 
        }

        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var evt in attachEvents) {
                if (evt.Type == AttachRecord.Kind.Call) {
                    storeIn.Add(evt.AsObj as Epoch);
                }
            }
            return storeIn;
        }

        Epoch Introspect.GetParent() {
            return parent;
        }

        readonly struct AttachRecord {
            public enum Kind : byte { Cleanup, Handle, Use, Call, Export }

            public readonly Kind Type;
            public readonly object AsObj;
            public readonly SpokeHandle Handle;

            public AttachRecord(SpokeHandle handle) {
                this = default;
                Type = Kind.Handle;
                Handle = handle;
            }

            public AttachRecord(Kind type, object asObj) {
                this = default;
                Type = type;
                AsObj = asObj;
            }

            public void Detach(Epoch that) {
                switch (Type) {
                    case Kind.Cleanup:
                        try {
                            (AsObj as Action)?.Invoke();
                        } catch (Exception e) {
                            SpokeError.Log($"Cleanup failed in '{that}'", e);
                        }
                        break;
                    case Kind.Handle:
                        Handle.Dispose();
                        break;
                    case Kind.Use:
                        try {
                            (AsObj as IDisposable).Dispose();
                        } catch (Exception e) {
                            SpokeError.Log($"Dispose failed in '{that}'", e);
                        }
                        break;
                    case Kind.Call:
                        try {
                            (AsObj as Epoch.Friend).Detach();
                        } catch (Exception e) {
                            SpokeError.Log($"Failed to cleanup child of '{that}': {AsObj}", e);
                        }
                        break;
                }
            }
        }

        internal struct EpochMutations {
            Epoch owner;

            public EpochMutations(Epoch owner) {
                this.owner = owner;
            }

            public SpokeHandle Use(SpokeHandle handle) {
                NoMischief(); 
                owner.attachEvents.Add(new(handle)); 
                return handle;
            }

            public T Use<T>(T disposable) where T : IDisposable {
                NoMischief(); 
                owner.attachEvents.Add(new(AttachRecord.Kind.Use, disposable)); 
                return disposable;
            }

            public T Call<T>(T epoch) where T : Epoch {
                NoMischief();
                if (epoch.parent != null) {
                    throw new InvalidOperationException("Tried to attach an epoch which was already attached");
                }
                owner.attachEvents.Add(new(AttachRecord.Kind.Call, epoch));
                var childCoords = owner.Coords.Extend(owner.attachEvents.Count - 1);
                var childTicker = (owner as Ticker) ?? owner.ticker;
                (epoch as Friend).Attach(owner, childCoords, childTicker, null);
                return epoch;
            }

            public T Export<T>(T obj) {
                NoMischief();
                owner.attachEvents.Add(new(AttachRecord.Kind.Export, obj));
                return obj;
            }

            public bool TryImport<T>(out T obj) {
                obj = default(T);
                var startIndex = owner.attachEvents.Count - 1;
                for (var anc = owner; anc != null; startIndex = anc.attachIndex, anc = anc.parent) {
                    for (var i = startIndex; i >= 0; i--) {
                        var evt = anc.attachEvents[i];
                        if (evt.Type == AttachRecord.Kind.Export && evt.AsObj is T o) {
                            obj = o;
                            return true;
                        }
                    }
                }
                return false;
            }

            public T Import<T>() {
                if (TryImport(out T obj)) return obj;
                throw new Exception($"Failed to import: {typeof(T).Name}");
            }

            public void OnCleanup(Action fn) {
                NoMischief(); 
                owner.attachEvents.Add(new(AttachRecord.Kind.Cleanup, fn));
            }

            public void RequestTick() {
                owner.controlHandle.OnPopSelf(owner._requestTick);
            }

            void NoMischief() {
                if (!owner.controlHandle.IsTop) {
                    throw new InvalidOperationException("Tried to mutate an Epoch that's been sealed for further changes.");
                }
            }
        }
    }

    public struct EpochBuilder {
        Epoch.EpochMutations s;

        internal EpochBuilder(Epoch.EpochMutations s) { 
            this.s = s; 
        }

        public SpokeHandle Use(SpokeHandle handle) 
            => s.Use(handle);

        public T Use<T>(T disposable) where T : IDisposable 
            => s.Use(disposable);

        public T Call<T>(T epoch) where T : Epoch 
            => s.Call(epoch);

        public T Export<T>(T obj) 
            => s.Export(obj);

        public bool TryImport<T>(out T obj) 
            => s.TryImport(out obj);
            
        public T Import<T>() 
            => s.Import<T>();

        public void OnCleanup(Action fn) 
            => s.OnCleanup(fn);

        public void Log(string msg) 
            => s.Call(new LambdaEpoch($"Log: {msg}", s => {
                if (!s.TryImport<ISpokeLogger>(out var logger)) logger = SpokeError.DefaultLogger;
                logger?.Log($"{msg}\n\n{SpokeIntrospect.TreeTrace(SpokeRuntime.Frames)}");
                return null;
            }));

        public EpochPorts Ports => new(s);
    }

    public struct EpochPorts {
        Epoch.EpochMutations s;

        internal EpochPorts(Epoch.EpochMutations s) { 
            this.s = s; 
        }

        public void RequestTick() 
            => s.RequestTick();
    }
}