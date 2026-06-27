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
            var cam = GetComponent<Camera>();
            var velocity = Vector3.zero;

            // Orbit rig: the target is the board point the camera looks at, and the camera
            // sits a fixed offset above and behind it. Tilt, height and distance never
            // change while panning, so capture the rig offset once and drive the camera
            // position from the target.
            var target = transform.position;
            var rigOffset = Vector3.zero;
            {
                var plane = new Plane(Vector3.up, GameState.Instance.LevelBounds.center);
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (plane.Raycast(ray, out var enter)) {
                    target = ray.GetPoint(enter);
                    rigOffset = transform.position - target;
                }
            }

            IEnumerator onUpdate() {
                while (true) {
                    // Pan the target across the board plane, relative to the camera's facing.
                    var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    var right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

                    var input = Vector3.zero;
                    if (Input.GetKey(KeyCode.W)) input += forward;
                    if (Input.GetKey(KeyCode.S)) input -= forward;
                    if (Input.GetKey(KeyCode.D)) input += right;
                    if (Input.GetKey(KeyCode.A)) input -= right;

                    // Ease velocity toward the input so it ramps up on press and glides to a
                    // stop on release. The exp() keeps the easing frame-rate independent.
                    var targetVelocity = input.normalized * panSpeed;
                    velocity = Vector3.Lerp(velocity, targetVelocity, 1f - Mathf.Exp(-acceleration * Time.deltaTime));
                    target += velocity * Time.deltaTime;

                    // Keep the looked-at point inside the level, then place the camera.
                    var bounds = GameState.Instance.LevelBounds;
                    target.x = Mathf.Clamp(target.x, bounds.min.x, bounds.max.x);
                    target.z = Mathf.Clamp(target.z, bounds.min.z, bounds.max.z);
                    transform.position = target + rigOffset;

                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };
    }
}