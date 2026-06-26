using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class CameraControls : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] float panSpeed = 10f;
        [SerializeField] float acceleration = 16f;
        [SerializeField] float overscroll = 2f;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(PanControls);
            });  
        }

        EffectBlock PanControls => s => {
            var cam = GetComponent<Camera>();
            var velocity = Vector3.zero;

            // Clamp so the camera's view never spills past the level edge. The visible
            // footprint is where the four screen corners land on the board plane; reading
            // it from the live projection keeps it correct across aspect ratios and resizes.
            Vector3 ClampToBoard(Vector3 pos) {
                var bounds = GameState.Instance.LevelBounds;
                var plane = new Plane(Vector3.up, bounds.center);
                var camPos = cam.transform.position;
                float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                for (var i = 0; i < 4; i++) {
                    var ray = cam.ViewportPointToRay(new Vector3(i & 1, (i >> 1) & 1, 0f));
                    if (!plane.Raycast(ray, out var enter)) continue;
                    var hit = ray.GetPoint(enter);
                    minX = Mathf.Min(minX, hit.x); maxX = Mathf.Max(maxX, hit.x);
                    minZ = Mathf.Min(minZ, hit.z); maxZ = Mathf.Max(maxZ, hit.z);
                }

                // The footprint moves rigidly with the camera, so the allowed range is the
                // level shrunk by the footprint's reach on each side. If the level is
                // smaller than the view on an axis, centre on it instead of clamping.
                float Axis(float p, float footLo, float footHi, float boardLo, float boardHi) {
                    var lo = boardLo - footLo;
                    var hi = boardHi - footHi;
                    return lo <= hi ? Mathf.Clamp(p, lo, hi) : (boardLo + boardHi) * 0.5f;
                }
                pos.x = Axis(pos.x, minX - camPos.x, maxX - camPos.x, bounds.min.x - overscroll, bounds.max.x + overscroll);
                pos.z = Axis(pos.z, minZ - camPos.z, maxZ - camPos.z, bounds.min.z - overscroll, bounds.max.z + overscroll);
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