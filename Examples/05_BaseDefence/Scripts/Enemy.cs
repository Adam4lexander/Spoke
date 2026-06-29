using System;
using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Enemy : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] MeshShatterFX shatterFX;
        [SerializeField] GameObject fireFrom;
        [SerializeField] GameObject showOnTracked;

        [Header("Attributes")]
        [SerializeField] float radius;
        [SerializeField] float moveSpeed = 2f;
        [SerializeField] float stopDistance = 1.2f;   // gap kept from the target building's centre

        State<Vector3> position = new();

        public Health Health => health;

        protected override void Init(EffectBuilder s) {
            position.Set(transform.position);
            showOnTracked.SetActive(false);

            s.Phase(IsEnabled, s => {
                s.Phase(health.IsAlive, s => {
                    s.Effect(Advance);
                    s.Effect(RadarTrack);
                    s.Effect(Bob);
                });

                var isDead = s.Memo(s => !s.D(health.IsAlive));
                s.Phase(isDead, s => {
                    shatterFX.StartFX();
                    s.Effect(s => {
                        if (s.D(shatterFX.IsFinished)) Destroy(gameObject);
                    });
                });
            });
        }

        EffectBlock Advance => s => {
            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    // Head for the building nearest our current position.
                    var buildings = GameState.Buildings;
                    Building target = null;
                    var bestSqr = float.MaxValue;
                    for (var i = 0; i < buildings.Count; i++) {
                        var sqr = (buildings[i].Payload.Position.Now - transform.position).sqrMagnitude;
                        if (sqr < bestSqr) { bestSqr = sqr; target = buildings[i].Payload; }
                    }
                    if (target) {
                        var to = target.Position.Now - transform.position;
                        to.y = 0f;
                        var dist = to.magnitude;
                        if (dist > stopDistance) {
                            var dir = to / dist;
                            var step = Mathf.Min(moveSpeed * Time.deltaTime, dist - stopDistance);
                            transform.position += dir * step;
                            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        }
                    }
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        EffectBlock RadarTrack => s => {
            var sensor = s.Use(GameState.RadarZone.Add(default, new Circle(position.Now, radius), detects: true, detectable: false));
            s.Effect(s => sensor.Circle = new Circle(s.D(position), radius));

            var isTracked = s.Memo(s => sensor.Overlaps.Count > 0, sensor.Changed);
            s.Phase(isTracked, s => {
                showOnTracked.SetActive(true);
                s.OnCleanup(() => showOnTracked.SetActive(false));

                var collider = s.Use(GameState.TrackedEnemyZone.Add(this, new Circle(position.Now, radius)));
                s.Effect(s => collider.Circle = new Circle(s.D(position), radius));
            });
        };

        EffectBlock Bob => s => {
            const float bobAmplitude = 0.05f;
            const float bobSpeed = 6f;
            IEnumerator onUpdate() {
                var baseY = transform.position.y;
                var phase = UnityEngine.Random.value * Mathf.PI * 2f;   // desync enemies so they don't bob in lockstep
                while (true) {
                    yield return null;
                    var p = transform.position;
                    p.y = baseY + Mathf.Sin(Time.time * bobSpeed + phase) * bobAmplitude;
                    transform.position = p;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        void Update() {
            position.Set(transform.position);
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}