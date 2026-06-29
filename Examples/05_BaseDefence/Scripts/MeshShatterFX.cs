using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class MeshShatterFX : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject meshRoot;

        [Header("Attributes")]
        [SerializeField] float gravity = 9.8f;
        [SerializeField] float blastSpeed = 10f;
        [SerializeField] float time = 2f;

        [Header("Runtime (Dont Edit)")]
        [SerializeField] UState<bool> isStarted = new(false);
        [SerializeField] UState<bool> isFinished = new(false);

        public ISignal<bool> IsStarted => isStarted;
        public ISignal<bool> IsFinished => isFinished;

        public void StartFX() => isStarted.Set(true);

        protected override void Init(EffectBuilder s) {
            var shouldPlay = s.Memo(s => {
                return s.D(IsEnabled) && s.D(IsStarted) && !s.D(IsFinished);
            });
            s.Phase(shouldPlay, PlayFX);
        }

        struct MeshState {
            public readonly Transform Obj;
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
            public MeshState(Transform obj, Vector3 velocity, Vector3 angularVelocity) {
                Obj = obj;
                Velocity = velocity;
                AngularVelocity = angularVelocity;
            }
        }

        EffectBlock PlayFX => s => {
            var renderers = meshRoot.GetComponentsInChildren<Renderer>();

            // Blast pieces away from the combined center of the mesh, so the shatter
            // reads as an explosion radiating outward rather than a uniform drift.
            var center = Vector3.zero;
            foreach (var r in renderers) center += r.bounds.center;
            if (renderers.Length > 0) center /= renderers.Length;

            var states = new List<MeshState>();
            foreach (var r in renderers) {
                var piece = r.transform;
                var outward = piece.position - center;
                if (outward.sqrMagnitude < 0.0001f) outward = Random.onUnitSphere;
                // Bias upward and jitter the direction so pieces arc instead of sliding
                // flat along the blast axis.
                var dir = (outward.normalized + Vector3.up * 0.5f + Random.onUnitSphere * 0.25f).normalized;
                var velocity = dir * blastSpeed * Random.Range(0.7f, 1.3f);
                var angularVelocity = Random.onUnitSphere * Random.Range(180f, 540f);
                states.Add(new MeshState(piece, velocity, angularVelocity));
            }

            var timer = 0f;
            IEnumerator onUpdate() {
                while (true) {
                    var dt = Time.deltaTime;
                    for (int i = 0; i < states.Count; i++) {
                        var state = states[i];
                        if (!state.Obj) continue;
                        state.Velocity += Vector3.down * gravity * dt;
                        state.Obj.position += state.Velocity * dt;
                        state.Obj.Rotate(state.AngularVelocity * dt, Space.World);
                        states[i] = state;
                    }
                    if (timer >= time) isFinished.Set(true);
                    timer += dt;
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };
    }
}