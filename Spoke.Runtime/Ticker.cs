using System.Collections.Generic;
using System;

namespace Spoke {

    /// <summary>
    /// Tickers are epochs that act as execution gateways driving ticks to descending epochs.
    /// All epochs (including Tickers) request ticks from their nearest ancestor Ticker.
    /// Tickers decide the tempo of ticks delivered to their descendants, but not the order they're ticked in.
    /// Pending epochs are sorted by their tick-cursor coordinate, to enforce an imperative execution order.
    /// </summary>
    public abstract class Ticker : Epoch, Ticker.Friend {

        new internal interface Friend { 
            Epoch TickNext(); 
            void Schedule(Epoch epoch); 
            void SetIsPaused(bool value); 
            void SetToManual(); 
        }
        
        // Priority queue of pending epochs that requested a tick.
        Heap<Epoch> pending = new((a, b) => a.CompareTo(b));
        // List of OnTick callbacks, declared in Bootstrap()
        List<Action<TickContext>> onTick = new();
        Action requestTick;
        bool isPaused;
        bool isManual;
        bool didContinue;

        SpokeRuntime.Handle controlHandle => (this as Epoch.Friend).GetControlHandle();
        bool isTicking => controlHandle.IsAlive && controlHandle.Frame.Type == SpokeRuntime.FrameKind.Tick;

        // Tickers shouldn't auto-arm ticks after Init, because they request ticks on receiving pending epochs
        protected override sealed bool AutoArmTickAfterInit => false;

        /// <summary>
        /// Override to configure and wire up the tickers behaviour. Bootstrap is called during Init,
        /// and it's passed a builder DSL thats only valid during the bootstrap block.
        /// The returned epoch is attached directly under the ticker, and it's the root of the tickers managed tree.
        /// </summary>
        protected abstract Epoch Bootstrap(TickerBuilder s);

        // Wires up the ticker behaviour, invokes Bootstrap, and attaches the returned epoch.
        protected sealed override TickBlock Init(EpochBuilder s) {
            // SpokeTree will set isManual=true for Manual trees. They shouldn't request ticks automatically.
            if (!isManual) {
                requestTick = () => s.Ports.RequestTick();
                s.OnCleanup(() => requestTick = null);
            }
            var root = Bootstrap(new TickerBuilder(s, new(this)));
            s.Call(root); // Attach the epoch returned by Bootstrap
            // Declare a TickBlock that invokes each OnTick callback in order, once per tick.
            return s => {
                if (isPaused || !HasPending()) return;
                didContinue = false;
                foreach (var fn in onTick) {
                    if (!isPaused) {
                        fn?.Invoke(new TickContext(s, this));
                    }
                }
                if (!didContinue && !isPaused) {
                    throw new Exception("Ticker must TickNext() or Pause() during OnTick, or it risks infinite flushes.");
                }
                // After processing OnTick callbacks, if there are still pending epochs, request another tick.
                if (HasPending() && !isPaused) {
                    requestTick?.Invoke();
                }
            };
        }

        // Delivers a single tick to the next pending epoch, returning that epoch.
        Epoch Friend.TickNext() {
            if (!isTicking) {
                throw new Exception("TickNext() must be called from within an OnTick block");
            } 
            didContinue = true; // TickNext was called at least once
            var ticked = pending.RemoveMin();
            (ticked as Epoch.Friend).Tick();
            return ticked;
        }

        // Epochs schedule themselves by calling this method on their nearest ancestor ticker.
        void Friend.Schedule(Epoch epoch) {
            var prevHasPending = HasPending();
            pending.Insert(epoch);
            if (!isTicking && !prevHasPending && HasPending() && !isPaused) {
                requestTick?.Invoke();
            } 
        }

        // Pausing a ticker prevents it from requesting ticks.
        void Friend.SetIsPaused(bool value) {
            if (isPaused == value) return;
            isPaused = value;
            if (value == false && HasPending() && !isTicking) {
                // Request a tick if we received pending epochs while paused
                requestTick?.Invoke();
            }
        }

        // Used by SpokeTree in manual mode to disable automatic tick requests.
        void Friend.SetToManual() {
            isManual = true;
        }

        bool HasPending() {
            while (pending.Count > 0 && pending.PeekMin().IsDetached) {
                pending.RemoveMin();
            }
            return pending.Count > 0;
        }

        // Exposes mutation operations available during Bootstrap and OnTick blocks.
        internal struct Mutator {
            public Ticker Ticker { get; }
            public bool HasPending => Ticker?.HasPending() ?? false;
            public Epoch Next => Ticker?.pending.PeekMin();

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

    /// <summary>
    /// A DSL builder for configuring a Ticker during its Bootstrap block.
    /// Only valid during Bootstrap. Mutation methods throw if called outside that block.
    /// The exception is TickerBuilder.Ports, which may be captured and used later.
    /// </summary>
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

    /// <summary>
    /// Ports for controlling a Ticker. Can be captured and used outside the Bootstrap block.
    /// </summary>
    public struct TickerPorts {
        Ticker.Mutator r;
        
        internal TickerPorts(Ticker.Mutator r) { 
            this.r = r; 
        }
        
        /// <summary>True when there is at least one epoch pending a tick</summary>
        public bool HasPending => r.HasPending;

        /// <summary>Peeks at the next epoch that will be ticked, or null if none pending</summary>
        public Epoch PeekNext() {
            return r.Next;
        }

        /// <summary>Paused tickers won't request ticks and won't call OnTick actions</summary>
        public void Pause() {
            (r.Ticker as Ticker.Friend).SetIsPaused(true);
        }

        /// <summary>Resumes a paused ticker, allowing it to deliver ticks again</summary>
        public void Resume() {
            (r.Ticker as Ticker.Friend).SetIsPaused(false);
        }
    }

    /// <summary>
    /// DSL object passed to OnTick callbacks, allowing them to drive ticks to pending epochs.
    /// </summary>
    public struct TickContext {
        Ticker t;

        internal TickContext(EpochBuilder s, Ticker t) { 
            this.t = t; 
        }

        public Epoch TickNext() 
            => (t as Ticker.Friend).TickNext();
    }
}