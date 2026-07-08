using System;
using System.Collections;

namespace Spoke.Examples.BaseDefence {

    // Extend Spoke by adding extension methods to EffectBuilder: they become new s.Xxx(...) calls
    // usable inside any Init, exactly like Spoke's built-in ones. Here we add s.Coroutine(...),
    // which runs a Unity coroutine that's stopped automatically when its effect unmounts.
    public static class SpokeExtensions {

        /// <summary>
        /// Runs a coroutine for as long as the surrounding effect is mounted, then stops it for you.
        /// Unity stops coroutines when their GameObject is disabled, so mount this under an
        /// IsEnabled phase. The guards below will let you know if you forget.
        /// </summary>
        public static void Coroutine(this EffectBuilder s, IEnumerator routine) {
            s.Effect("Coroutine", s => {
                // The SpokeBehaviour running this tree. That's who we start the coroutine on.
                var host = s.Import<UnityContext>().Behaviour as SpokeBehaviour;
                if (host == null) {
                    throw new InvalidOperationException("s.Coroutine: this tree has no SpokeBehaviour host to run coroutines on");
                }
                if (s.D(host.IsEnabled) == false) {
                    throw new InvalidOperationException($"s.Coroutine: host '{host.name}' is disabled. Coroutines must be mounted under an IsEnabled phase");
                }
                var running = host.StartCoroutine(routine);
                s.OnCleanup(() => host.StopCoroutine(running));
            });
        }

        /// <summary>Shorthand for the common case: runs action every frame while the effect is mounted.</summary>
        public static void Coroutine(this EffectBuilder s, Action action) {
            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    action?.Invoke();
                }
            }
            s.Coroutine(onUpdate());
        }
    }
}
