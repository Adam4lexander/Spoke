using UnityEngine;

namespace Spoke.Examples {

    public class SpokeState : SpokeBehaviour {

        [Header("References")]
        [SerializeField] MeshRenderer Sphere;

        // State<T> holds reactive state — like a variable you can subscribe to.
        // Any logic that *uses* it will re-run when its value changes.
        State<bool> isRed = State.Create(false);

        protected override void Init(EffectBuilder s) {

            // Cache the material once (to avoid triggering Unity's internal instancing).
            var sphereMaterial = Sphere.material;

            // Reactively update the sphere’s color when `isRed` changes.
            s.UseEffect(s => {
                // `s.D(...)` tracks a *dynamic dependency* -- this effect will re-run if `isRed` changes.
                sphereMaterial.color = s.D(isRed) ? Color.red : Color.blue;
            });

            /*
            // This version is equivalent, but uses *explicit* dependency tracking.
            // Slightly more verbose, but clearer in some cases.
            s.UseEffect(s => {
                sphereMaterial.color = isRed.Now ? Color.red : Color.blue;
            }, isRed);
            */
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
