using System;
using System.Collections.Generic;

namespace Spoke {

    public delegate void EffectBlock(EffectBuilder s);
    public delegate IRef<T> EffectBlock<T>(EffectBuilder s);

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

    public struct EffectBuilder {

        Action<ITrigger> addDynamicTrigger;
        EpochBuilder s;

        public EffectBuilder(Action<ITrigger> addDynamicTrigger, EpochBuilder s) {
            this.addDynamicTrigger = addDynamicTrigger;
            this.s = s;
        }

        public T D<T>(ISignal<T> signal) { 
            addDynamicTrigger(signal); 
            return signal.Now; 
        }

        public void Use(SpokeHandle trigger) => s.Use(trigger);
        public T Use<T>(T disposable) where T : IDisposable => s.Use(disposable);
        public T Call<T>(T epoch) where T : Epoch => s.Call(epoch);
        public T Export<T>(T obj) => s.Export(obj);
        public bool TryImport<T>(out T obj) => s.TryImport(out obj);
        public T Import<T>() => s.Import<T>();
        public void OnCleanup(Action fn) => s.OnCleanup(fn);
        public void Log(string msg) => s.Log(msg);
    }

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

    public static partial class DockExtensions {
        public static void Effect(this Dock dock, object key, EffectBlock block, params ITrigger[] triggers)
            => dock.Call(key, new Effect("Effect", block, triggers));
        public static void Effect(this Dock dock, string name, object key, EffectBlock block, params ITrigger[] triggers)
            => dock.Call(key, new Effect(name, block, triggers));
    }
}