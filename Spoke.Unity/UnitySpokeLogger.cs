using UnityEngine;

namespace Spoke {

    public class UnitySpokeLogger : ISpokeLogger {

        public Object context;

        public UnitySpokeLogger(Object context = null) { 
            this.context = context; 
        }

        public void Log(string msg) => WithoutUnityStackTrace(LogType.Log, () => Debug.Log(msg, context));

        public void Error(string msg) => WithoutUnityStackTrace(LogType.Error, () => Debug.LogError(msg, context));

        void WithoutUnityStackTrace(LogType logType, System.Action action) {
            var original = Application.GetStackTraceLogType(logType);
            Application.SetStackTraceLogType(logType, StackTraceLogType.None);
            action?.Invoke();
            Application.SetStackTraceLogType(logType, original);
        }
    }
}