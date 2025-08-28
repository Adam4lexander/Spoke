using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
using System.Reflection;
#endif

namespace Spoke {

    /// <summary>Static factory for UState<T></summary>
    public static class UState {
        public static UState<T> Create<T>(T val = default) => new UState<T>(val);
    }

    /// <summary>
    /// A reactive state container that is serializable by Unity.
    /// It's serializable in the Unity Editor, and will show up in the Inspector.
    /// You can use it to expose editor-configurable state in your MonoBehaviours and ScriptableObjects.
    /// 
    /// This was quite tricky to get right, due to how Unity handles serialization.
    /// But it does seem to handle all the edge cases, including the Undo/Redo system.
    /// </summary>
    [Serializable]
    public class UState<T> : IState<T>, ISerializationCallbackReceiver {
        [SerializeField] T value; // Serialized value can be compared with runtimeValue to detect editor changes
        T runtimeValue; // True value of the state
        Trigger<T> trigger = new Trigger<T>();
        bool isInitialized = false;
        int setCount;

        public UState() { }

        public UState(T value) {
            this.value = runtimeValue = value;
        }

        /// <summary>The current value of the UState</summary>
        public T Now { 
            get { 
                EnsureInitialized(); 
                return runtimeValue; 
            } 
        }

        /// <summary>Subscribes to value changes, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action action) { 
            EnsureInitialized(); 
            return trigger.Subscribe(action); 
        }

        /// <summary>Subscribes to value changes, returns unsubscribe handle</summary>
        public SpokeHandle Subscribe(Action<T> action) { 
            EnsureInitialized(); 
            return trigger.Subscribe(action); 
        }

        /// <summary>Unsubscribe the action. Prefer SpokeHandle.Dispose() instead</summary>
        public void Unsubscribe(Action action) { 
            EnsureInitialized(); 
            trigger.Unsubscribe(action); 
        }

        /// <summary>Unsubscribe the action. Prefer SpokeHandle.Dispose() instead</summary>
        public void Unsubscribe(Action<T> action) { 
            EnsureInitialized(); 
            trigger.Unsubscribe(action); 
        }
        
        /// <summary>Sets the value, notify subscribers if it changed</summary>
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

        /// <summary>Updates the value by a function of the previous value</summary>
        public void Update(Func<T, T> setter) {
            if (setter != null) Set(setter(Now));
        }

        public void OnBeforeSerialize() { }

        // Detects changes made in the editor, including Undo/Redo
        public void OnAfterDeserialize() {
            if (!isInitialized) {
                runtimeValue = value;
                return;
            }
            var newValue = value;
            var storeSetCount = ++setCount;
#if UNITY_EDITOR
            // Delay call, so trigger.Invoke() happens outside of deserialization
            // Many Unity APIs are not safe to call during deserialization
            EditorApplication.delayCall += () => {
                if (setCount > storeSetCount) return;
                Set(newValue);
            };
#else
            Set(newValue);
#endif
        }

        // Lazy initialize the state on first access by user code. Avoids a whole lot of
        // complexity in the Unity serialization lifecycle.
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

    /// <summary>
    /// Base class for property drawers that unwrap a serialized value from a wrapper class.
    /// Used by UState to show the inner value in the inspector.
    /// Otherwise it would appear as { Now: value }
    /// </summary>
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
}