using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Pool : SpokeSingleton<Pool> {

        readonly Dictionary<GameObject, Stack<GameObject>> idle = new();
        readonly Dictionary<GameObject, GameObject> origin = new();

        public static GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot = default) {
            var pool = Instance;
            if (pool.idle.TryGetValue(prefab, out var stack) && stack.Count > 0) {
                var reused = stack.Pop();
                reused.transform.SetPositionAndRotation(pos, rot);
                reused.SetActive(true);
                return reused;
            }
            var instance = Object.Instantiate(prefab, pos, rot);
            pool.origin[instance] = prefab;
            return instance;
        }

        public static void Despawn(GameObject instance) {
            var pool = Instance;
            if (!pool.origin.TryGetValue(instance, out var prefab)) {
                Destroy(instance);
                return;
            }
            instance.SetActive(false);
            if (!pool.idle.TryGetValue(prefab, out var stack)) pool.idle[prefab] = stack = new();
            stack.Push(instance);
        }

        protected override void Init(EffectBuilder s) { }
    }
}