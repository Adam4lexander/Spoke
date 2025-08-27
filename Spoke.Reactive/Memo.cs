using System;

namespace Spoke {

    public delegate T MemoBlock<T>(MemoBuilder s);

    public class Memo<T> : Computation, ISignal<T> {

        State<T> state = State.Create<T>();
        Action<ITrigger> _addDynamicTrigger;
        Action<MemoBuilder> block;

        public T Now => state.Now;

        public Memo(string name, MemoBlock<T> selector, params ITrigger[] triggers) : base(name, triggers) {
            block = s => {
                if (selector != null) {
                    state.Set(selector(s));
                }
            };
            _addDynamicTrigger = AddDynamicTrigger;
        }

        protected override void OnRun(EpochBuilder s) {
            var builder = new MemoBuilder(_addDynamicTrigger, s);
            block(builder);
        }

        public SpokeHandle Subscribe(Action action) => state.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => state.Subscribe(action);
        public void Unsubscribe(Action action) => state.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => state.Unsubscribe(action);
    }

    public struct MemoBuilder {

        Action<ITrigger> addDynamicTrigger;
        EpochBuilder s;

        internal MemoBuilder(Action<ITrigger> addDynamicTrigger, EpochBuilder s) {
            this.addDynamicTrigger = addDynamicTrigger;
            this.s = s;
        }

        public U D<U>(ISignal<U> signal) { 
            addDynamicTrigger(signal); 
            return signal.Now; 
        }

        public void OnCleanup(Action fn) => s.OnCleanup(fn);
    }
}