using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Spoke {

#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class SpokeUnityBootstrap {
        static bool isInitialized;

        static SpokeUnityBootstrap() 
            => Init();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            if (isInitialized) return;
            isInitialized = true;
            SpokeError.Log = (msg, ex) => Debug.LogError($"[Spoke] {msg}\n{ex}");
            SpokeError.DefaultLogger = new UnitySpokeLogger();
        }
    }
}