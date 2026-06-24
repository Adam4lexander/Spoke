using UnityEngine;

namespace Spoke.Examples {

    public class State_Example : SpokeBehaviour {

        [Header("References")]
        [SerializeField] MeshRenderer Sphere;

        // State<T> is like a reactive variable you can subscribe to.
        // When its value changes all subscribers will be notified.
        State<bool> isRed = State.Create(false);

        protected override void Init(EffectBuilder s) {

            // Cache the material once (to avoid triggering Unity's internal instancing).
            var sphereMaterial = Sphere.material;

            // Reactively update the sphere's color when `isRed` changes.
            s.Effect(s => {
                // s.D(...) tracks a dynamic dependency -- this effect will re-run if `isRed` changes.
                sphereMaterial.color = s.D(isRed) ? Color.red : Color.blue;
            });

            // You could write the effect with explicit dependencies, instead of using s.D(...)
            /*
            s.Effect(s => {
                sphereMaterial.color = isRed.Now ? Color.red : Color.blue;
            }, isRed); // Dependency `isRed` given explicitly
            */
            // Generally s.D(...) is preferred though
        }

        void Update() {
            // Flip the color each time space is pressed
            if (Input.GetKeyDown(KeyCode.Space)) {
                isRed.Update(val => !val);
                // Or: isRed.Set(!isRed.Now);
            }
        }
    }
}
