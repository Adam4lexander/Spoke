using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spoke {

    /// <summary>
    /// Base class for singletons, which extend SpokeBehaviour.
    /// Based on typical Unity singleton patterns.
    /// 
    /// example: public class GameManager : SpokeSingleton<GameManager> { ... }
    /// </summary>
    public abstract class SpokeSingleton<T> : SpokeBehaviour where T : SpokeSingleton<T> {
        
        /// <summary>
        /// Retrieves the singleton instance, or creates it if it doesn't exist.
        /// If accessed outside of play mode, returns null and logs a warning.
        /// </summary>
        public static T Instance {
            get {
                if (!Application.isPlaying) {
                    Debug.LogWarning($"{typeof(T).Name} was accessed outside of play mode. Ignoring.");
                    return null;
                }
                EnsureStaticInit();
                if (!IsDestroyed.Now && !instance.Now) {
                    FindOrCreateInstance();
                }
                return instance.Now;
            }
        }

        public static ISignal<bool> IsDestroyed => isDestroyed;

        static State<T> instance = State.Create<T>();
        static State<bool> isDestroyed = State.Create(false);
        static bool isInitialized = false;
        static Scene instanceScene;

        // Override to change default 'DontDestroyOnLoad' behaviour
        protected virtual bool OverrideDontDestroyOnLoad => false;

        // Override to change default GameObject name
        protected virtual string OverrideName => $"[!-------{typeof(T).Name}-------!]";

        static void EnsureStaticInit() {
            if (isInitialized) return;
            isInitialized = true;
            SpokeTeardown.App.Subscribe(() => isDestroyed.Set(false));
            SceneManager.sceneUnloaded += scene => {
                if (scene != instanceScene) return;
                instanceScene = default;
                isDestroyed.Set(false);
            };
        }

        static void FindOrCreateInstance() {
            T nextInstance;
            var managers = FindObjectsOfType(typeof(T)) as T[];
            if (managers.Length == 0) {
                var go = new GameObject();
                nextInstance = go.AddComponent<T>();
                if (nextInstance.OverrideDontDestroyOnLoad) DontDestroyOnLoad(go);
                go.name = nextInstance.OverrideName;
            } else {
                nextInstance = managers[0];
            }
            instance.Set(nextInstance);
        }

        protected override void Awake() {
            if (instance.Now != null && instance.Now != this) {
                Debug.LogError($"Deleting duplicate instance of singleton {typeof(T).Name}");
                Destroy(gameObject);
                return;
            }
            instanceScene = gameObject.scene;
            instance.Set(this as T);
            base.Awake();
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            if (instance.Now == this) isDestroyed.Set(true);
        }
    }
}