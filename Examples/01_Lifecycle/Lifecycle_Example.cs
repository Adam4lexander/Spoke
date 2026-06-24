using UnityEngine;

namespace Spoke.Examples {
    
    public class Lifecycle_Example : SpokeBehaviour {
     
        protected override void Init(EffectBuilder s) {

            // Init is mounted in Awake, and cleaned up in OnDestroy
            Debug.Log("Awake Mounted");
            s.OnCleanup(() => Debug.Log("Disposing Awake"));

            // Runs while the behaviour is Enabled (from OnEnable to OnDisable)
            s.Phase(IsEnabled, s => {
                Debug.Log("IsEnabled Mounted");
                s.OnCleanup(() => Debug.Log("Disposing IsEnabled"));
            });

            // Runs while the behaviour is Started (from Start to OnDestroy)
            s.Phase(IsStarted, s => {
                Debug.Log("IsStarted Mounted");
                s.OnCleanup(() => Debug.Log("Disposing IsStarted"));
            });
        }
    }
}
