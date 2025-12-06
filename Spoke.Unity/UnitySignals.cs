using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Spoke {

    /// <summary>
    /// Reactive signals for Unity application and scene lifecycle events.
    /// </summary>
    public static class UnitySignals {
        static State<bool> isPlaying = State.Create(Application.isPlaying);
        static Trigger<Scene> sceneTeardown = Trigger.Create<Scene>();
        static Trigger appTeardown = Trigger.Create();

        /// <summary>
        /// Reactive signal tracking Application.isPlaying.
        /// </summary>
        public static ISignal<bool> IsPlaying => isPlaying;

        /// <summary>
        /// Triggers before a scene is unloaded.
        /// Requires manual wiring - see NotifySceneTeardown() below.
        /// </summary>
        public static ITrigger<Scene> SceneTeardown => sceneTeardown;

        /// <summary>
        /// Triggers when the application is quitting, exiting play mode in the editor,
        /// or before domain reload in the editor.
        /// </summary>
        public static ITrigger AppTeardown => appTeardown;

        /// <summary>
        /// Call this before unloading a scene to trigger the SceneTeardown event.
        /// Unity does not provide a built-in event before a scene is unloaded.
        ///
        /// example:
        ///
        ///     public void ChangeScene(string nextScene) {
        ///         UnitySignals.NotifySceneTeardown(SceneManager.GetActiveScene());
        ///         SceneManager.LoadScene(nextScene);
        ///    }
        /// </summary>
        public static void NotifySceneTeardown(Scene s) => sceneTeardown.Invoke(s);

        static SpokeTree tree;

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize() {
            if (tree != null) return;
            tree = SpokeTree.Spawn("UnitySignals", new Effect("Init", s => {
                Application.quitting += () => appTeardown.Invoke();
#if UNITY_EDITOR
                EditorApplication.playModeStateChanged += state => {
                    isPlaying.Set(Application.isPlaying);
                    if (state == PlayModeStateChange.ExitingPlayMode) appTeardown.Invoke();
                };
                AssemblyReloadEvents.beforeAssemblyReload += () => appTeardown.Invoke();
#endif
            }));
        }
    }
}