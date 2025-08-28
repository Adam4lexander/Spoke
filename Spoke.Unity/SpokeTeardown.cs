using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Spoke {

    /// <summary>
    /// Static teardown triggers for Unity application and scene lifecycle events.
    /// You can use these to clean up resources when the application is quitting,
    /// or when a scene is unloaded.
    /// 
    /// This is safer then using OnDisable or OnDestroy in MonoBehaviours, which
    /// may sometimes be called after already destroying dependants. Causing errors
    /// when trying to clean up.
    /// </summary>
    public static class SpokeTeardown {
        static Trigger<Scene> scene = Trigger.Create<Scene>();
        static Trigger app = Trigger.Create();

        /// <summary>
        /// Triggers when a scene is unloaded.
        /// SpokeBehaviour automatically subscribes to this to teardown when its scene is unloaded.
        /// Unfortunately requires some manual wiring, see SignalScene() below.
        /// </summary>
        public static ITrigger<Scene> Scene { 
            get { 
                EnsureInit(); 
                return scene; 
            } 
        }

        /// <summary>
        /// Triggers when the application is quitting, or exiting play mode in the editor,
        /// or before domain reload in the editor.
        /// </summary>
        public static ITrigger App { 
            get { 
                EnsureInit(); 
                return app; 
            } 
        }
        
        /// <summary>
        /// You must manually call this when unloading a scene, to trigger the Scene teardown event.
        /// Unity does not provide any built-in event before a scene is unloaded.
        /// 
        /// example:
        ///     
        ///     public void ChangeScene(string nextScene) {
        ///         SpokeTeardown.SignalScene(SceneManager.GetActiveScene());
        ///         SceneManager.LoadScene(nextScene);
        ///    }
        /// 
        /// This is completely optional though, only if you want to SpokeTeardown to clean up safely
        /// </summary>
        public static void SignalScene(Scene scene) { 
            EnsureInit(); 
            SpokeTeardown.scene.Invoke(scene); 
        }
        
        static bool isInitialized = false;

        static void EnsureInit() {
            if (isInitialized) return;
            isInitialized = true;
            Application.quitting += () => app.Invoke();
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += state => { 
                if (state == PlayModeStateChange.ExitingPlayMode) app.Invoke(); 
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => app.Invoke();
#endif
        }
    }
}