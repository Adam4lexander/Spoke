using System;
using System.Collections.Generic;

namespace Spoke {

    /// <summary>Delegate type for building an Effect, Phase or Reaction</summary>
    public delegate void EffectBlock(EffectBuilder s);
    /// <summary>Delegate type for building an Effect<T></summary>
    public delegate IRef<T> EffectBlock<T>(EffectBuilder s);

    /// <summary>
    /// Abstract base class for Effect, Phase and Reaction
    /// </summary>
    public abstract class BaseEffect : Computation {
        protected EffectBlock block;
        Action<ITrigger> _addDynamicTrigger;

        public BaseEffect(string name, IEnumerable<ITrigger> triggers) : base(name, triggers) {
            _addDynamicTrigger = AddDynamicTrigger;
        }

        protected override void OnRun(EpochBuilder s) {
            block?.Invoke(new EffectBuilder(_addDynamicTrigger, s));
        }
    }

    /// <summary>
    /// DSL-style builder passed into EffectBlocks
    /// It's only valid to use during execution of the EffectBlock
    /// Most methods simply forward to the underlying EpochBuilder
    /// </summary>
    public struct EffectBuilder {
        Action<ITrigger> addDynamicTrigger;
        EpochBuilder s;

        public EffectBuilder(Action<ITrigger> addDynamicTrigger, EpochBuilder s) {
            this.addDynamicTrigger = addDynamicTrigger;
            this.s = s;
        }

        /// <summary>
        /// Dynamically adds a signal dependency to the effect, and returns its current value
        /// The effect will re-run when the signal changes value
        /// </summary>
        public T D<T>(ISignal<T> signal) { 
            addDynamicTrigger(signal); 
            return signal.Now; 
        }

        /// <summary>Take ownership of a SpokeHandle for auto-cleanup. SpokeHandle is non-allocating</summary>
        public void Use(SpokeHandle trigger) 
            => s.Use(trigger);

        /// <summary>Take ownership of a IDisposable for auto-cleanup</summary>
        public T Use<T>(T disposable) where T : IDisposable 
            => s.Use(disposable);

        /// <summary>Attaches any subclass of Epoch as a child with bound lifetime</summary>
        public T Call<T>(T epoch) where T : Epoch 
            => s.Call(epoch);

        /// <summary>Export a lexically scoped object that can be imported further down the tree</summary>
        public T Export<T>(T obj) 
            => s.Export(obj);

        /// <summary>Try to import a lexically scoped object of type T</summary>
        public bool TryImport<T>(out T obj) 
            => s.TryImport(out obj);

        /// <summary>Import a lexically scoped object of type T, throws if not found</summary>
        public T Import<T>() 
            => s.Import<T>();

        /// <summary>Attach a cleanup function, executed before the effect reruns or is detached</summary>
        public void OnCleanup(Action fn) 
            => s.OnCleanup(fn);

        /// <summary>Logs a message and dumps the Spoke Tree Trace.</summary>
        public void Log(string msg) 
            => s.Log(msg);
    }

    /// <summary>
    /// Extension methods for EffectBuilder
    /// Provides convenience methods for common types of attachments
    /// You can define your own extension methods by following this pattern
    /// </summary>
    public static partial class EffectBuilderExtensions {

        public static void Subscribe(this EffectBuilder s, ITrigger trigger, Action action)
            => s.Use(trigger != null ? trigger.Subscribe(action) : default);

        public static void Subscribe<T>(this EffectBuilder s, ITrigger<T> trigger, Action<T> action)
            => s.Use(trigger != null ? trigger.Subscribe(action) : default);

        public static ISignal<T> Memo<T>(this EffectBuilder s, MemoBlock<T> selector, params ITrigger[] triggers)
            => s.Call(new Memo<T>("Memo", selector, triggers));

        public static ISignal<T> Memo<T>(this EffectBuilder s, string name, MemoBlock<T> selector, params ITrigger[] triggers)
            => s.Call(new Memo<T>(name, selector, triggers));

        public static ISignal<T> Effect<T>(this EffectBuilder s, EffectBlock<T> block, params ITrigger[] triggers)
            => s.Call(new Effect<T>("Effect", block, triggers));

        public static ISignal<T> Effect<T>(this EffectBuilder s, string name, EffectBlock<T> block, params ITrigger[] triggers)
            => s.Call(new Effect<T>(name, block, triggers));

        public static void Effect(this EffectBuilder s, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Effect("Effect", block, triggers));

        public static void Effect(this EffectBuilder s, string name, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Effect(name, block, triggers));

        public static void Reaction(this EffectBuilder s, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Reaction("Reaction", block, triggers));

        public static void Reaction(this EffectBuilder s, string name, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Reaction(name, block, triggers));

        public static void Phase(this EffectBuilder s, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Phase("Phase", mountWhen, block, triggers));

        public static void Phase(this EffectBuilder s, string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers)
            => s.Call(new Phase(name, mountWhen, block, triggers));

        public static Dock Dock(this EffectBuilder s)
            => s.Call(new Dock("Dock"));

        public static Dock Dock(this EffectBuilder s, string name)
            => s.Call(new Dock(name));
    }

    /// <summary>
    /// Extension methods for Dock
    /// </summary>
    public static partial class DockExtensions {

        public static void Effect(this Dock dock, object key, EffectBlock block, params ITrigger[] triggers)
            => dock.Call(key, new Effect("Effect", block, triggers));
            
        public static void Effect(this Dock dock, object key, string name, EffectBlock block, params ITrigger[] triggers)
            => dock.Call(key, new Effect(name, block, triggers));
    }
}