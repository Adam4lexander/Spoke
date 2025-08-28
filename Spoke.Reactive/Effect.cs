using System;

namespace Spoke {

    /// <summary>
    /// An Effect runs an EffectBlock, and then re-runs whenever any of its triggers fire
    /// </summary>
    public sealed class Effect : BaseEffect {

        public Effect(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = block;
        }
    }

    /// <summary>
    /// An Effect<T> is similar to Memo<T>, in that it's a reactive signal
    /// However, it's EffectBlock<T> returns an ISignal<T>, and not a raw T value like memos do
    /// It's also capable of attaching its own sub-effects, memos or cleanup logic
    /// This makes Effect<T> nestable and composable
    /// </summary> 
    public sealed class Effect<T> : BaseEffect, ISignal<T> {
        State<T> state = State.Create<T>();

        public T Now => state.Now;

        public Effect(string name, EffectBlock<T> block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = Mount(block);
        }

        EffectBlock Mount(EffectBlock<T> block) => s => {
            if (block == null) return;
            var result = block.Invoke(s);
            if (result is ISignal<T> signal) {
                s.Subscribe(signal, x => state.Set(x));
            }
            s.Call(new LambdaEpoch("Deferred Initializer", s => s => state.Set(result.Now)));
        };

        public SpokeHandle Subscribe(Action action) 
            => state.Subscribe(action);

        public SpokeHandle Subscribe(Action<T> action) 
            => state.Subscribe(action);

        public void Unsubscribe(Action action) 
            => state.Unsubscribe(action);

        public void Unsubscribe(Action<T> action) 
            => state.Unsubscribe(action);
    }
}