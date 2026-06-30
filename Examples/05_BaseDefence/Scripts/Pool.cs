using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Pool : SpokeSingleton<Pool> {

        [System.Serializable]
        struct PrewarmEntry {
            public GameObject prefab;
            public int count;
        }

        [SerializeField] PrewarmEntry[] prewarm;

        Dictionary<GameObject, Stack<GameObject>> idle = new();
        Dictionary<GameObject, GameObject> origin = new();

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

        protected override void Init(EffectBuilder s) {
            if (prewarm.Length == 0) return;

            // Instantiate under an inactive parent so prewarmed instances never run Awake/OnEnable
            // until they're actually spawned — prewarm pre-allocates, it must not run game logic.
            var nursery = new GameObject($"{name} (prewarm)");
            nursery.SetActive(false);
            foreach (var entry in prewarm) {
                if (entry.prefab == null) continue;
                for (int i = 0; i < entry.count; i++) {
                    var instance = Object.Instantiate(entry.prefab, nursery.transform);
                    origin[instance] = entry.prefab;
                    Despawn(instance);                    // deactivate + push onto the idle stack
                    instance.transform.SetParent(null);   // re-home out of the nursery while inactive
                }
            }
            Destroy(nursery);
        }
    }
}