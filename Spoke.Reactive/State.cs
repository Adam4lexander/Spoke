using System.Collections.Generic;
using System;

namespace Spoke {

    public interface IRef<out T> {
        T Now { get; }
    }

    public interface ISignal<out T> : IRef<T>, ITrigger<T> { }

    public interface IState<T> : ISignal<T> {
        void Set(T value);
        void Update(Func<T, T> setter);
    }

    public static class State {
        public static State<T> Create<T>(T val = default) => new State<T>(val);
    }

    public class State<T> : IState<T> {

        T value;
        Trigger<T> trigger = new Trigger<T>();

        public State() { }

        public State(T value) { 
            Set(value); 
        }

        public T Now => value;

        public SpokeHandle Subscribe(Action action) => trigger.Subscribe(action);
        public SpokeHandle Subscribe(Action<T> action) => trigger.Subscribe(action);
        public void Unsubscribe(Action action) => trigger.Unsubscribe(action);
        public void Unsubscribe(Action<T> action) => trigger.Unsubscribe(action);

        public void Set(T value) {
            if (EqualityComparer<T>.Default.Equals(value, this.value)) return;
            this.value = value;
            trigger.Invoke(value);
        }

        public void Update(Func<T, T> setter) {
            if (setter != null) {
                Set(setter(Now));
            }
        }
    }
}