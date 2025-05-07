using UnityEngine;

namespace Spoke.Examples {
    
    public class HelloSpoke : SpokeBehaviour {
     
        protected override void Init(EffectBuilder s) {

            // This block runs when the behaviour is Awake
            s.UsePhase(IsAwake, s => {
                Debug.Log("IsAwake Mounted");
                s.OnCleanup(() => Debug.Log("Disposing IsAwake"));
            });

            // This block runs when the behaviour is Enabled
            s.UsePhase(IsEnabled, s => {
                Debug.Log("IsEnabled Mounted");
                s.OnCleanup(() => Debug.Log("Disposing IsEnabled"));
            });

            // This block runs when the behaviour is Started
            s.UsePhase(IsStarted, s => {
                Debug.Log("IsStarted Mounted");
                s.OnCleanup(() => Debug.Log("Disposing IsStarted"));
            });

            // Init is also a declarative block, just like the phases above.
            // It runs before the behaviour is Awake.
            Debug.Log("Init Mounted");
            s.OnCleanup(() => Debug.Log("Disposing Init"));

        }

    }

}
