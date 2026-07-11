using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Spoke {

/// <summary>
/// Static bootstrap for Spoke in Unity.
/// Initializes SpokeError logging to use Unity's Debug.LogError.
/// That's where Spoke logs internal errors by default.
/// Also configures the default Spoke logger: UnityContext doubles as the logger,
/// and one with no host behaviour logs to the console without pinging an object.
/// </summary>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class SpokeUnityBootstrap {
        static bool isInitialized;

        static SpokeUnityBootstrap()
            => EnsureInitialized();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void EnsureInitialized() {
            if (isInitialized) return;
            isInitialized = true;
            SpokeError.Log = (msg, ex) => Debug.LogError($"[Spoke] {msg}\n{ex}");
            SpokeError.DefaultLogger = new UnityContext();
        }
    }
}