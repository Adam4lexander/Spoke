using UnityEngine;

namespace Spoke.Examples {
    
    public class Lifecycle : SpokeBehaviour {
     
        protected override void Init(EffectBuilder s) {

            // Runs while the behaviour is Awake (from Awake to OnDestroy)
            s.Phase(IsAwake, s => {
                Debug.Log("IsAwake Mounted");
                s.OnCleanup(() => Debug.Log("Disposing IsAwake"));
            });

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

            // Init runs first -- before Awake -- and behaves like a permanent effect
            Debug.Log("Init Mounted");
            s.OnCleanup(() => Debug.Log("Disposing Init"));
        }
    }
}
