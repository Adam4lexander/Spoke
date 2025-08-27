using System;

namespace Spoke {

    public sealed class Effect : BaseEffect {

        public Effect(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = block;
        }
    }

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