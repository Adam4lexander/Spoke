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

        public T Now { 
            get { 
                EnsureInitialized(); 
                return runtimeValue; 
            } 
        }

        public SpokeHandle Subscribe(Action action) { 
            EnsureInitialized(); 
            return trigger.Subscribe(action); 
        }

        public SpokeHandle Subscribe(Action<T> action) { 
            EnsureInitialized(); 
            return trigger.Subscribe(action); 
        }

        public void Unsubscribe(Action action) { 
            EnsureInitialized(); 
            trigger.Unsubscribe(action); 
        }

        public void Unsubscribe(Action<T> action) { 
            EnsureInitialized(); 
            trigger.Unsubscribe(action); 
        }
        
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
}