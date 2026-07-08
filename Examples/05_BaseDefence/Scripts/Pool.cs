using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // I included an object pool in this game for realism, and because object pooling introduces a
    // lifecycle-management problem (which is Spoke's strength).
    // Pools are a classic source of lifecycle bugs: a reused object can carry state left over from its
    // previous life if something wasn't reset on despawn.
    // This is exactly the surface area for bugs that Spoke can eliminate.

    /// <summary>A minimal object pool. Despawn disables an instance and stashes it; Spawn re-enables an idle one, or instantiates a new one.</summary>
    public class Pool : SpokeSingleton<Pool> {

        [System.Serializable]
        struct PrewarmEntry {
            public GameObject prefab;
            public int count;
        }

        [SerializeField] PrewarmEntry[] prewarm;

        Dictionary<GameObject, Stack<GameObject>> idle = new();
        Dictionary<GameObject, GameObject> origin = new();

        /// <summary>Returns an active instance of prefab — reused from its idle pool if one's free, otherwise freshly instantiated.</summary>
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

        /// <summary>Disables an instance and returns it to its prefab's idle pool for reuse. Destroys it instead if it never came from the pool.</summary>
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