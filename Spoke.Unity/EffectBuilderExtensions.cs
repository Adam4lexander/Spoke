using UnityEngine.Events;

namespace Spoke {

    /// <summary>
    /// EffectBuilder extensions specific to Unity.
    /// </summary>
    public static partial class EffectBuilderExtensions {

        /// <summary>Subscribes a UnityEvent, automatically unsubscribes</summary>
        public static void Subscribe(this EffectBuilder s, UnityEvent evt, UnityAction fn) {
            evt.AddListener(fn);
            s.OnCleanup(() => evt.RemoveListener(fn));
        }

        /// <summary>Subscribes a UnityEvent<T>, automatically unsubscribes</summary>
        public static void Subscribe<T>(this EffectBuilder s, UnityEvent<T> evt, UnityAction<T> fn) {
            evt.AddListener(fn);
            s.OnCleanup(() => evt.RemoveListener(fn));
        }
    }
}