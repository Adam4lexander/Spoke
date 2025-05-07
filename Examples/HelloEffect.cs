using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples {

    [Serializable]
    public struct Sphere {
        public Renderer renderer;
        public Text label;
        public void CloneMat() { var m = renderer.material; }
    }

    public class HelloEffect : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Sphere effectSphere;
        [SerializeField] Sphere outerPhaseSphere;
        [SerializeField] Sphere innerPhaseSphere;
        [SerializeField] Sphere reactionSphere;

        [Header("Attributes")]
        [SerializeField] UState<bool> mountOuterPhase = UState.Create(true);
        [SerializeField] UState<bool> mountInnerPhase = UState.Create(true);

        Trigger blinkCommand = Trigger.Create();

        protected override void Init(EffectBuilder s) {

            effectSphere.CloneMat(); 
            outerPhaseSphere.CloneMat(); 
            innerPhaseSphere.CloneMat(); 
            reactionSphere.CloneMat();

            s.UseEffect(BlinkSphere("Effect", effectSphere), 
                blinkCommand);

            s.UsePhase(mountOuterPhase, s => {

                s.UseEffect(BlinkSphere("Phase (Outer)", outerPhaseSphere), 
                    blinkCommand);

                s.UsePhase(mountInnerPhase, BlinkSphere("Phase (Inner)", innerPhaseSphere), 
                    blinkCommand);

            }, blinkCommand);

            s.UseReaction(BlinkSphere("Reaction", reactionSphere), 
                blinkCommand);
        }

        EffectBlock BlinkSphere(string label, Sphere sphere) => s => {

            var origLabel = sphere.label.text;
            sphere.label.text = label;
            s.OnCleanup(() => sphere.label.text = origLabel);

            IEnumerator blinkRoutine() {
                sphere.renderer.material.color = Color.green;
                yield return new WaitForSeconds(0.5f);
                sphere.renderer.material.color = Color.blue;
            }
            var routineInstance = StartCoroutine(blinkRoutine());
            s.OnCleanup(() => {
                StopCoroutine(routineInstance);
                sphere.renderer.material.color = Color.red;
            });
        };

        void Update() {
            if (Input.GetKeyDown(KeyCode.Space)) {
                blinkCommand.Invoke();
            }
        }
    }
}