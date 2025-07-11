using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples {

    [Serializable]
    public struct Sphere {
        public Renderer renderer;
        public Text label;
        // Force Unity to clone shared material instance
        public void CloneMat() { _ = renderer.material; }
    }

    public class SpokeEffect : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Sphere effectSphere;
        [SerializeField] Sphere outerPhaseSphere;
        [SerializeField] Sphere innerPhaseSphere;
        [SerializeField] Sphere reactionSphere;

        [Header("Attributes")]
        // UState<T> is the serialized version of State<T> -- same reactive behavior, but visible in the Inspector
        [SerializeField] UState<bool> mountOuterPhase = UState.Create(true);
        [SerializeField] UState<bool> mountInnerPhase = UState.Create(true);

        // Trigger is the simplest reactive signal in Spoke -- a fire-and-forget pulse.
        // When invoked, any subscribed effects or memos will run.
        Trigger flashCommand = Trigger.Create();

        protected override void Init(EffectBuilder s) {

            // Ensure each sphere has a unique material instance
            effectSphere.CloneMat(); 
            outerPhaseSphere.CloneMat(); 
            innerPhaseSphere.CloneMat(); 
            reactionSphere.CloneMat();

            // Effect: Mounts immediately and remounts when any dependency is triggered
            s.Effect(FlashSphere("Effect", effectSphere), flashCommand);

            // Phase: Mounts only while `mountOuterPhase` is true. Remounts whenever any dependency changes.
            s.Phase(mountOuterPhase, s => {

                // This effect is scoped to the outer phase. It mounts when the outer phase is mounted.
                s.Effect(FlashSphere("Phase (Outer)", outerPhaseSphere));

                // This inner phase only mounts while `mountInnerPhase` is true, and will remount on trigger
                s.Phase(mountInnerPhase, FlashSphere("Phase (Inner)", innerPhaseSphere), flashCommand);

            }, flashCommand);

            // Reaction: Does *not* mount until a dependency is triggered.
            // It runs only when triggered -- perfect for one-shot logic that doesn't need to persist
            s.Reaction(FlashSphere("Reaction", reactionSphere), flashCommand);
        }

        // EffectBlock is a core Spoke abstraction — it's just:
        //     public delegate void EffectBlock(EffectBuilder s);
        //
        // You can return them like functions, allowing modular, parameterized logic.
        // This one blinks a sphere green, waits 0.5s, then turns it blue. On cleanup, resets to red.
        EffectBlock FlashSphere(string label, Sphere sphere) => s => {

            var origLabel = sphere.label.text;
            sphere.label.text = label;
            s.OnCleanup(() => sphere.label.text = origLabel);

            IEnumerator blinkRoutine() {
                sphere.renderer.sharedMaterial.color = Color.green;
                yield return new WaitForSeconds(0.5f);
                sphere.renderer.sharedMaterial.color = Color.blue;
            }

            var routineInstance = StartCoroutine(blinkRoutine());

            // Stop the flash if this scope is cleaned up early
            s.OnCleanup(() => {
                StopCoroutine(routineInstance);
                sphere.renderer.sharedMaterial.color = Color.red;
            });
        };

        void Update() {
            // Press space to invoke the blink trigger
            if (Input.GetKeyDown(KeyCode.Space)) {
                flashCommand.Invoke();
            }
        }
    }
}