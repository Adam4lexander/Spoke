using UnityEngine;

namespace Spoke.Examples {

    public class HelloState : SpokeBehaviour {

        [Header("References")]
        [SerializeField] MeshRenderer Sphere;

        // This holds reactive state -- true = red, false = blue.
        State<bool> isRed = State.Create(false);

        protected override void Init(EffectBuilder s) {

            // Cache the material once (to avoid triggering Unity's internal instancing).
            var sphereMaterial = Sphere.material;

            // Reactively update the colour whenever `isRed` changes.
            s.UseEffect(s => {
                // s.D tracks a dynamic dependency on `isRed` and reacts automatically
                sphereMaterial.color = s.D(isRed) ? Color.red : Color.blue;
            });

            /*
            // Equivalent with explicit dependency -- `isRed` is passed manually
            s.UseEffect(s => {
                sphereMaterial.color = isRed.Now ? Color.red : Color.blue;
            }, isRed);
            */
        }

        void Update() {
            // Toggle the state when the spacebar is pressed.
            if (Input.GetKeyDown(KeyCode.Space)) {
                isRed.Update(val => !val);
                // Or simply: isRed.Set(!isRed.Now);
            }
        }
    }
}
