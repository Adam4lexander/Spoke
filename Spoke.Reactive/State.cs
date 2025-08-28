using System.Collections.Generic;
using System;

namespace Spoke {

    /// <summary>
    /// A boxed value which can be read by (Now) but not modified
    /// </summary>
    public interface IRef<out T> {
        T Now { get; }
    }

    /// <summary>
    /// A Signal is a read-only reactive value with event subscriptions
    /// Its trigger invokes with the new value when it changes
    /// The current value can be read via Now
    /// </summary>
    public interface ISignal<out T> : IRef<T>, ITrigger<T> { }

    /// <summary>
    /// A State is a read-write reactive value with event subscriptions
    /// Its invokes subscribers with the new value when it changes
    /// </summary>
    public interface IState<T> : ISignal<T> {
        void Set(T value);
        void Update(Func<T, T> setter);
    }

    /// <summary>
    /// Static factory methods for State<T>
    /// ie: State.Create(5) or State.Create("hello")
    /// </summary>
    public static class State {
        public static State<T> Create<T>(T val = default) => new State<T>(val);
    }

    /// <summary>
    /// A State is a read-write reactive value with event subscriptions
    /// Its invokes subscribers with the new value when it changes
    /// </summary>
    public class State<T> : IState<T> {
        T value;
        Trigger<T> trigger = new Trigger<T>();

        /// <summary>The current value of the state</summary>
        public T Now => value;

        public State() { }

        public State(T value) { 
            Set(value); 
        }

        /// <summary>Subscribes to value changes, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action action) 
            => trigger.Subscribe(action);

        /// <summary>Subscribes to value changes, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action<T> action) 
            => trigger.Subscribe(action);

        /// <summary>Explicit alternative to unsubscribe the given action.</summary>
        public void Unsubscribe(Action action) 
            => trigger.Unsubscribe(action);

        /// <summary>Explicit alternative to unsubscribe the given action.</summary>
        public void Unsubscribe(Action<T> action) 
            => trigger.Unsubscribe(action);

        /// <summary>Sets the value, invoking the trigger if it changed</summary>
        public void Set(T value) {
            if (EqualityComparer<T>.Default.Equals(value, this.value)) return;
            this.value = value;
            trigger.Invoke(value);
        }

        /// <summary>Updates the value using the given a function of the present value</summary>
        public void Update(Func<T, T> setter) {
            if (setter == null) return;
            Set(setter(Now));
        }
    }
}