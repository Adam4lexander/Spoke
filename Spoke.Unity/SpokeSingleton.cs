using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

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
                if (!IsDestroyed.Now && !instance) {
                    FindOrCreateInstance();
                }
                return instance;
            }
        }

        public static ISignal<bool> IsDestroyed => isDestroyed;

        static State<bool> isDestroyed = State.Create(false);
        static bool isInitialized = false;
        static T instance;
        static Scene instanceScene;

        // Override to change default 'DontDestroyOnLoad' behaviour
        protected virtual bool OverrideDontDestroyOnLoad => false;

        // Override to change default GameObject name
        protected virtual string OverrideName => $"[!-------{typeof(T).Name}-------!]";

        // Override to disable auto-instantiation, for singletons that must be hand-placed
        // in the scene (e.g. to wire up serialized references)
        protected virtual bool OverrideAutoInstantiate => true;

        static void EnsureStaticInit() {
            if (isInitialized) return;
            isInitialized = true;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredEditMode) isDestroyed.Set(false);
            };
#endif
            SceneManager.sceneUnloaded += scene => {
                if (scene != instanceScene) return;
                instanceScene = default;
                isDestroyed.Set(false);
            };
        }

        static void FindOrCreateInstance() {
            var managers = FindObjectsOfType(typeof(T)) as T[];
            if (managers.Length > 0) {
                instance = managers[0];
                return;
            }

            // Built inactive so Awake/OnEnable don't fire until we've confirmed
            // this singleton is allowed to auto-instantiate
            var go = new GameObject();
            go.SetActive(false);
            var nextInstance = go.AddComponent<T>();
            if (!nextInstance.OverrideAutoInstantiate) {
                Debug.LogError($"{typeof(T).Name} instance not found in scene, and auto-instantiate is disabled.");
                Destroy(go);
                return;
            }

            if (nextInstance.OverrideDontDestroyOnLoad) DontDestroyOnLoad(go);
            go.name = nextInstance.OverrideName;
            instance = nextInstance;
            go.SetActive(true);
        }

        protected override void Awake() {
            if (instance != null && instance != this) {
                Debug.LogError($"Deleting duplicate instance of singleton {typeof(T).Name}");
                Destroy(gameObject);
                return;
            }
            instanceScene = gameObject.scene;
            instance = this as T;
            base.Awake();
        }

        protected override void OnDestroy() {
            base.OnDestroy();
            if (instance == this) isDestroyed.Set(true);
        }
    }
}