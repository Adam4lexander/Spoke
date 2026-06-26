using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class CameraControls : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] float panSpeed = 10f;
        [SerializeField] float acceleration = 16f;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(PanControls);
            });  
        }

        EffectBlock PanControls => s => {
            var velocity = Vector3.zero;
            IEnumerator onUpdate() {
                while (true) {
                    // Flatten the camera's axes onto the board plane, so the tilt doesn't
                    // drag us toward or away from the board as we pan.
                    var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    var right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

                    var input = Vector3.zero;
                    if (Input.GetKey(KeyCode.W)) input += forward;
                    if (Input.GetKey(KeyCode.S)) input -= forward;
                    if (Input.GetKey(KeyCode.D)) input += right;
                    if (Input.GetKey(KeyCode.A)) input -= right;

                    // Ease velocity toward the target so it ramps up on press and glides to
                    // a stop on release. The exp() keeps the easing frame-rate independent.
                    var target = input.normalized * panSpeed;
                    velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-acceleration * Time.deltaTime));
                    transform.position += velocity * Time.deltaTime;
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };
    }
}