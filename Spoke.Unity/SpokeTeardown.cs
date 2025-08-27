using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Spoke {

    public static class SpokeTeardown {

        static Trigger<Scene> scene = Trigger.Create<Scene>();
        static Trigger app = Trigger.Create();

        public static ITrigger<Scene> Scene { 
            get { 
                EnsureInit(); 
                return scene; 
            } 
        }

        public static ITrigger App { 
            get { 
                EnsureInit(); 
                return app; 
            } 
        }
        
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