using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class CameraControls : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Camera cam;

        [Header("Attributes")]
        [SerializeField] float panSpeed = 10f;
        [SerializeField] float acceleration = 16f;

        State<View> view = new();
        public ISignal<View> View => view;

        protected override void Init(EffectBuilder s) {
            s.Phase(IsEnabled, s => {
                s.Effect(PanControls);
            });  
        }

        EffectBlock PanControls => s => {
            var velocity = Vector3.zero;

            // Orbit rig: the target is the board point the camera looks at, and the camera
            // sits a fixed offset above and behind it. Tilt, height and distance never
            // change while panning, so capture the rig offset once and drive the camera
            // position from the target.
            var target = cam.transform.position;
            var rigOffset = Vector3.zero;
            {
                var plane = GameState.GroundPlane;
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (plane.Raycast(ray, out var enter)) {
                    target = ray.GetPoint(enter);
                    rigOffset = cam.transform.position - target;
                }
            }

            s.Coroutine(() => {
                // Pan the target across the board plane, relative to the camera's facing.
                var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;

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
                var bounds = GameState.LevelBounds;
                target.x = Mathf.Clamp(target.x, bounds.min.x, bounds.max.x);
                target.z = Mathf.Clamp(target.z, bounds.min.z, bounds.max.z);
                cam.transform.position = target + rigOffset;

                view.Set(new View(cam, GameState.GroundPlane));
            });
        };
    }
}