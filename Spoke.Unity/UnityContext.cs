using UnityEngine;

namespace Spoke {

    /// <summary>
    /// The Unity context of a SpokeTree: it carries the MonoBehaviour hosting the tree,
    /// and doubles as the tree's logger — messages ping that behaviour in the console.
    /// SpokeBehaviour exports one into its tree automatically; manually spawned trees can pass their own.
    /// </summary>
    public class UnityContext : ISpokeLogger {

        public readonly MonoBehaviour Behaviour;

        public UnityContext(MonoBehaviour behaviour = null) {
            Behaviour = behaviour;
        }

        public void Log(string msg)
            => WithoutUnityStackTrace(LogType.Log, () => Debug.Log(msg, Behaviour));

        public void Error(string msg)
            => WithoutUnityStackTrace(LogType.Error, () => Debug.LogError(msg, Behaviour));

        void WithoutUnityStackTrace(LogType logType, System.Action action) {
            var original = Application.GetStackTraceLogType(logType);
            Application.SetStackTraceLogType(logType, StackTraceLogType.None);
            action?.Invoke();
            Application.SetStackTraceLogType(logType, original);
        }
    }
}
