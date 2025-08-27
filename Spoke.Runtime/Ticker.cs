using System.Collections.Generic;
using System;

namespace Spoke {

    public abstract class Ticker : Epoch, Ticker.Friend {

        new internal interface Friend { 
            Epoch TickNext(); 
            void Schedule(Epoch epoch); 
            void SetIsPaused(bool value); 
            void SetToManual(); 
        }
        
        OrderedWorkStack<Epoch> pending = new((a, b) => b.CompareTo(a));
        List<Action<TickContext>> onTick = new();
        Action requestTick;
        bool isPaused;
        bool isManual;
        bool didContinue;

        SpokeRuntime.Handle controlHandle => (this as Epoch.Friend).GetControlHandle();
        bool isTicking => controlHandle.IsAlive && controlHandle.Frame.Type == SpokeRuntime.FrameKind.Tick;

        protected override sealed bool AutoArmTickAfterInit => false;

        protected abstract Epoch Bootstrap(TickerBuilder s);

        protected sealed override TickBlock Init(EpochBuilder s) {
            if (!isManual) {
                requestTick = () => s.Ports.RequestTick();
                s.OnCleanup(() => requestTick = null);
            }
            var root = Bootstrap(new TickerBuilder(s, new(this)));
            s.Call(root);
            return s => {
                if (isPaused || !pending.Has) return;
                didContinue = false;
                foreach (var fn in onTick) {
                    if (!isPaused) {
                        fn?.Invoke(new TickContext(s, this));
                    }
                }
                if (!didContinue && !isPaused) {
                    throw new Exception("Ticker must TickNext() or Pause() during OnTick, or it risks infinite flushes.");
                }
                if (pending.Has && !isPaused) {
                    requestTick?.Invoke();
                }
            };
        }

        Epoch Friend.TickNext() {
            if (!isTicking) {
                throw new Exception("TickNext() must be called from within an OnTick block");
            } 
            didContinue = true;
            var ticked = pending.Pop();
            (ticked as Epoch.Friend).Tick();
            return ticked;
        }

        void Friend.Schedule(Epoch epoch) {
            var prevHasPending = pending.Has;
            pending.Enqueue(epoch);
            if (!isTicking && !prevHasPending && pending.Has && !isPaused) {
                requestTick?.Invoke();
            } 
        }

        void Friend.SetIsPaused(bool value) {
            if (isPaused == value) return;
            isPaused = value;
            if (value == false) {
                requestTick?.Invoke();
            }
        }

        void Friend.SetToManual() {
            isManual = true;
        }

        internal struct Mutator {
            public Ticker Ticker { get; }
            public bool HasPending => Ticker?.pending.Has ?? false;
            public Epoch Next => Ticker?.pending.Peek();

            public Mutator(Ticker ticker) {
                Ticker = ticker;
            }

            public void OnTick(Action<TickContext> fn) { 
                NoMischief(); 
                Ticker.onTick.Add(fn); 
            }

            void NoMischief() {
                var isSealed = Ticker.controlHandle.IsTop == false || Ticker.controlHandle.Frame.Type != SpokeRuntime.FrameKind.Init;
                if (isSealed) {
                    throw new InvalidOperationException("Cannot mutate engine outside its bootstrap block");
                }
            }
        }
    }

    public struct TickerBuilder {
        EpochBuilder s;
        Ticker.Mutator r;

        internal TickerBuilder(EpochBuilder s, Ticker.Mutator es) { 
            this.s = s; 
            this.r = es; 
        }
        
        public void Use(SpokeHandle trigger) 
            => s.Use(trigger);

        public T Use<T>(T disposable) where T : IDisposable 
            => s.Use(disposable);

        public T Export<T>(T obj) 
            => s.Export(obj);

        public T Import<T>() 
            => s.Import<T>();

        public bool TryImport<T>(out T obj) 
            => s.TryImport(out obj);

        public void OnCleanup(Action fn) 
            => s.OnCleanup(fn);

        public void OnTick(Action<TickContext> fn) 
            => r.OnTick(fn);

        public TickerPorts Ports => new(r);
    }

    public struct TickerPorts {
        Ticker.Mutator r;
        
        internal TickerPorts(Ticker.Mutator r) { 
            this.r = r; 
        }
        
        public bool HasPending => r.HasPending;

        public Epoch PeekNext() {
            return r.Next;
        }

        public void Pause() {
            (r.Ticker as Ticker.Friend).SetIsPaused(true);
        }

        public void Resume() {
            (r.Ticker as Ticker.Friend).SetIsPaused(false);
        }
    }

    public struct TickContext {
        Ticker t;

        internal TickContext(EpochBuilder s, Ticker t) { 
            this.t = t; 
        }

        public Epoch TickNext() 
            => (t as Ticker.Friend).TickNext();
    }
}