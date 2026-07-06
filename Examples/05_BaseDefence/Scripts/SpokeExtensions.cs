using System;
using System.Collections;

namespace Spoke.Examples.BaseDefence {

    // Spoke's EffectBuilder DSL is extended by plain extension methods — this is
    // this project's own vocabulary, added without touching Spoke itself.
    public static class SpokeExtensions {

        /// <summary>
        /// Runs a coroutine on the tree's host behaviour for as long as the effect is mounted.
        /// Unity kills coroutines when their runner deactivates, so only call this from
        /// effects mounted under an IsEnabled phase — enforced by the guards below.
        /// </summary>
        public static void Coroutine(this EffectBuilder s, IEnumerator routine) {
            s.Effect("Coroutine", s => {
                // SpokeBehaviour hands its tree a UnitySpokeLogger built around itself,
                // so the logger's context is the MonoBehaviour that can host coroutines.
                var host = s.Import<UnitySpokeLogger>().context as SpokeBehaviour;
                if (host == null) {
                    throw new InvalidOperationException("s.Coroutine: this tree has no SpokeBehaviour host to run coroutines on");
                }
                if (s.D(host.IsEnabled) == false) {
                    throw new InvalidOperationException($"s.Coroutine: host '{host.name}' is disabled — coroutines must be mounted under an IsEnabled phase");
                }
                var running = host.StartCoroutine(routine);
                s.OnCleanup(() => host.StopCoroutine(running));
            });
        }

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
