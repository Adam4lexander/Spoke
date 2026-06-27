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

            // The camera's look-at point sits a fixed offset ahead of it on the board
            // plane — tilt and height never change while panning — so capture that offset
            // once instead of raycasting every frame.
            var lookOffset = Vector3.zero;
            {
                var plane = new Plane(Vector3.up, GameState.Instance.LevelBounds.center);
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (plane.Raycast(ray, out var enter)) lookOffset = ray.GetPoint(enter) - transform.position;
            }

            // Clamp the look-at point (position + offset) into the level, and correct the
            // camera position by the same amount. The correction is exactly zero while the
            // look-at is inside, so nothing drifts frame to frame.
            Vector3 ClampToBoard(Vector3 pos) {
                var bounds = GameState.Instance.LevelBounds;
                var focusX = pos.x + lookOffset.x;
                var focusZ = pos.z + lookOffset.z;
                pos.x += Mathf.Clamp(focusX, bounds.min.x, bounds.max.x) - focusX;
                pos.z += Mathf.Clamp(focusZ, bounds.min.z, bounds.max.z) - focusZ;
                return pos;
            }

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
                    transform.position = ClampToBoard(transform.position + velocity * Time.deltaTime);
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };
    }
}