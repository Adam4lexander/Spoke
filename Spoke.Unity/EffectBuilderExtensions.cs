using UnityEngine.Events;

namespace Spoke {

    public static partial class EffectBuilderExtensions {

        public static void Subscribe(this EffectBuilder s, UnityEvent evt, UnityAction fn) {
            evt.AddListener(fn);
            s.OnCleanup(() => evt.RemoveListener(fn));
        }

        public static void Subscribe<T>(this EffectBuilder s, UnityEvent<T> evt, UnityAction<T> fn) {
            evt.AddListener(fn);
            s.OnCleanup(() => evt.RemoveListener(fn));
        }
    }
}