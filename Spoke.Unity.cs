// Spoke.Unity.cs
// -----------------------------
// > EffectBuilderExtensions
// > SpokeTeardown
// > SpokeBehaviour
// > SpokeSingleton
// > UState
// > Drawers
// > UnitySpokeLogger

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
using System.Reflection;
#endif

namespace Spoke {

    // ============================== EffectBuilderExtensions ============================================================
    public static partial class EffectBuilderExtensions {
        public static void Subscribe(this EffectBuilder s, UnityEvent evt, UnityAction fn) {
            evt.AddListener(fn);
            s.OnCleanup(() => evt.RemoveListener(fn));
        }
        public static void Subscribe<T>(this EffectBuilder s, UnityEvent<T> evt, UnityAction<T> fn) {
            evt.AddListener(fn);
            s.OnCleanup(() => evt.RemoveListener(fn));
        }
    }
    // ============================== SpokeTeardown ============================================================
    public static class SpokeTeardown {
        static Trigger<Scene> scene = Trigger.Create<Scene>();
        static Trigger app = Trigger.Create();
        public static ITrigger<Scene> Scene { get { EnsureInit(); return scene; } }
        public static ITrigger App { get { EnsureInit(); return app; } }
        public static void SignalScene(Scene scene) { EnsureInit(); SpokeTeardown.scene.Invoke(scene); }
        static bool isInitialized = false;
        static void EnsureInit() {
            if (isInitialized) return;
            isInitialized = true;
            Application.quitting += () => app.Invoke();
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) app.Invoke(); };
            AssemblyReloadEvents.beforeAssemblyReload += () => app.Invoke();
#endif
        }
    }
    // ============================== SpokeBehaviour ============================================================
    public abstract class SpokeBehaviour : MonoBehaviour {
        State<bool> isAwake = State.Create(false);
        State<bool> isEnabled = State.Create(false);
        State<bool> isStarted = State.Create(false);
        public ISignal<bool> IsAwake => isAwake;
        public ISignal<bool> IsEnabled => isEnabled;
        public ISignal<bool> IsStarted => isStarted;
        static RootContainer container = new RootContainer();
        SpokeHandle root, sceneTeardown, appTeardown;
        protected abstract void Init(EffectBuilder s);
        protected virtual void Awake() {
            DoInit();
        }
        protected virtual void OnDestroy() {
            DoTeardown();
        }
        protected virtual void OnEnable() {
            if (root == null) DoInit(); // Domain reload may not run Awake
            isEnabled.Set(true);
        }
        protected virtual void OnDisable() {
            isEnabled.Set(false);
        }
        protected virtual void Start() {
            isStarted.Set(true);
        }
        void DoInit() {
            root = container.Engine($"{GetType().Name}", Init);
            sceneTeardown = SpokeTeardown.Scene.Subscribe(scene => { if (scene == gameObject.scene) DoTeardown(); });
            appTeardown = SpokeTeardown.App.Subscribe(() => DoTeardown());
            isAwake.Set(true);
        }
        void DoTeardown() {
            sceneTeardown.Dispose();
            appTeardown.Dispose();
            // Avoid setting enabled=false for ExecuteAlways behaviours on domain reload. Because
            // it will be serialized as disabled on each reload.
            if (Application.isPlaying) enabled = false;
            isEnabled.Set(false);
            root.Dispose();
            isAwake.Set(false);
        }
        class InitEffect : Epoch {
            EffectBlock block;
            public InitEffect(string name, EffectBlock block) {
                Name = name;
                this.block = block;
            }
            protected override ExecBlock Init(EpochBuilder s) {
                Action<ITrigger> addDynamicTrigger = _ => {
                    throw new InvalidOperationException("Cannot call D() from Init");
                };
                block?.Invoke(new EffectBuilder(addDynamicTrigger, s));
                return null;
            }
        }
        class RootContainer {
            Dock dock;
            long idx;
            public RootContainer() {
                SpokeRoot.Create(new SpokeEngine("root", s => {
                    dock = s.Dock();
                }));
            }
            public SpokeHandle Engine(string name, EffectBlock block) {
                var myId = idx++;
                dock.Call(myId, new SpokeEngine(name, new InitEffect("Init", block)));
                return SpokeHandle.Of(myId, myId => dock.Drop(myId));
            }
        }
    }
    // ============================== SpokeSingleton ============================================================
    public abstract class SpokeSingleton<T> : SpokeBehaviour where T : SpokeSingleton<T> {
        public static T Instance {
            get {
                if (!Application.isPlaying) {
                    Debug.LogWarning($"{typeof(T).Name} was accessed outside of play mode. Ignoring.");
                    return null;
                }
                EnsureStaticInit();
                if (!IsDestroyed.Now && !instance.Now) FindOrCreateInstance();
                return instance.Now;
            }
        }
        public static ISignal<bool> IsDestroyed => isDestroyed;
        static State<T> instance = State.Create<T>();
        static State<bool> isDestroyed = State.Create(false);
        static bool isInitialized = false;
        static Scene instanceScene;
        protected virtual bool OverrideDontDestroyOnLoad => false;
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
        protected override void Init(EffectBuilder s) {
            s.OnCleanup(() => {
                if (instance.Now == this) isDestroyed.Set(true);
            });
        }
    }
    // ============================== UState ============================================================
    public static class UState {
        public static UState<T> Create<T>(T val = default) => new UState<T>(val);
    }
    [Serializable]
    public class UState<T> : IState<T>, ISerializationCallbackReceiver {
        [SerializeField] T value;
        T runtimeValue;
        Trigger<T> trigger = new Trigger<T>();
        bool isInitialized = false;
        int setCount;
        public UState() { }
        public UState(T value) {
            this.value = runtimeValue = value;
        }
        public T Now { get { EnsureInitialized(); return runtimeValue; } }
        public SpokeHandle Subscribe(Action action) { EnsureInitialized(); return trigger.Subscribe(action); }
        public SpokeHandle Subscribe(Action<T> action) { EnsureInitialized(); return trigger.Subscribe(action); }
        public void Unsubscribe(Action action) { EnsureInitialized(); trigger.Unsubscribe(action); }
        public void Unsubscribe(Action<T> action) { EnsureInitialized(); trigger.Unsubscribe(action); }
        public void Set(T value) {
            setCount++;
            EnsureInitialized();
            if (EqualityComparer<T>.Default.Equals(value, runtimeValue)) {
                return;
            }
            this.value = value;
            runtimeValue = value;
            trigger.Invoke(value);
        }
        public void Update(Func<T, T> setter) {
            if (setter != null) Set(setter(Now));
        }
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() {
            if (!isInitialized) {
                runtimeValue = value;
                return;
            }
            var newValue = value;
            var storeSetCount = ++setCount;
#if UNITY_EDITOR
            EditorApplication.delayCall += () => {
                if (setCount > storeSetCount) return;
                Set(newValue);
            };
#else
            Set(newValue);
#endif
        }
        void EnsureInitialized() {
            if (!isInitialized) {
                isInitialized = true;
                runtimeValue = value;
            }
        }
    }
#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(UState<>), true)]
    public class StateDrawer : UnwrappedValueDrawer {
        public override SerializedProperty GetValueProperty(SerializedProperty property) => property.FindPropertyRelative("value");
    }
#endif
    // ============================== Drawers ============================================================
#if UNITY_EDITOR
    public abstract class UnwrappedValueDrawer : PropertyDrawer {
        public abstract SerializedProperty GetValueProperty(SerializedProperty property);
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var val = GetValueProperty(property);
            if (val == null) {
                EditorGUI.LabelField(position, label, new GUIContent("Not serializable"));
                return;
            }
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(position, val, label, true);
            if (EditorGUI.EndChangeCheck()) property.serializedObject.ApplyModifiedProperties();
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            var val = GetValueProperty(property);
            if (val == null) return EditorGUIUtility.singleLineHeight;
            return EditorGUI.GetPropertyHeight(val, label, true);
        }
        public static object GetParent(SerializedProperty prop) {
            var path = prop.propertyPath.Replace(".Array.data[", "[");
            object obj = prop.serializedObject.targetObject;
            var elements = path.Split('.');
            foreach (var element in elements.Take(elements.Length - 1))
                if (element.Contains("[")) {
                    var elementName = element.Substring(0, element.IndexOf("["));
                    var index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValue(obj, elementName, index);
                } else {
                    obj = GetValue(obj, element);
                }
            return obj;
        }
        static object GetValue(object source, string name) {
            if (source == null) return null;
            var type = source.GetType();
            var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (f == null) {
                var p = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p == null) return null;
                return p.GetValue(source, null);
            }
            return f.GetValue(source);
        }
        static object GetValue(object source, string name, int index) {
            var enumerable = GetValue(source, name) as IEnumerable;
            var enm = enumerable.GetEnumerator();
            while (index-- >= 0) enm.MoveNext();
            return enm.Current;
        }
    }
#endif
    // ============================== UnitySpokeLogger ============================================================
    public class UnitySpokeLogger : ISpokeLogger {
        public UnityEngine.Object context;
        public UnitySpokeLogger(UnityEngine.Object context = null) { this.context = context; }
        public void Log(string msg) => WithoutUnityStackTrace(LogType.Log, () => Debug.Log(msg, context));
        public void Error(string msg) => WithoutUnityStackTrace(LogType.Error, () => Debug.LogError(msg, context));
        void WithoutUnityStackTrace(LogType logType, Action action) {
            var original = Application.GetStackTraceLogType(logType);
            Application.SetStackTraceLogType(logType, StackTraceLogType.None);
            action?.Invoke();
            Application.SetStackTraceLogType(logType, original);
        }
    }
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public static class SpokeUnityBootstrap {
        static bool isInitialized;
        static SpokeUnityBootstrap() => Init();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            if (isInitialized) return;
            isInitialized = true;
            SpokeError.Log = (msg, ex) => Debug.LogError($"[Spoke] {msg}\n{ex}");
            SpokeError.DefaultLogger = new UnitySpokeLogger();
        }
    }
}