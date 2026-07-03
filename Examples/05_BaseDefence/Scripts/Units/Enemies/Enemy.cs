using System;
using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Enemy : SpokeBehaviour {

        [Header("Prefabs")]
        [SerializeField] GameObject bombBlastPrefab;

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] MeshFX meshFX;
        [SerializeField] GameObject fireFrom;
        [SerializeField] GameObject showOnTracked;

        [Header("Attributes")]
        [SerializeField] float radius;
        [SerializeField] float moveSpeed = 2f;
        [SerializeField] float stopDistance = 1.2f;   // gap kept from the target building's centre
        [SerializeField] float fireRate = 1f;         // shots per second

        public Health Health => health;

        protected override void Init(EffectBuilder s) {
            showOnTracked.SetActive(false);

            s.Phase(IsEnabled, s => {
                s.Phase(health.IsAlive, s => {
                    s.Effect(RadarTrack);
                    s.Effect(Bob);
                    s.Subscribe(health.Damaged, () => meshFX.Blink(Color.red));

                    var target = s.Effect(ChooseTarget);
                    s.Effect(s => {
                        if (s.D(target) == null) return;
                        s.Effect(Attack(s.D(target)));
                    });
                });

                var isDead = s.Memo(s => !s.D(health.IsAlive));
                s.Phase(isDead, s => {
                    meshFX.Shatter();
                    s.OnCleanup(meshFX.Restore);
                    s.Effect(s => {
                        if (s.D(meshFX.IsShattered)) Pool.Despawn(gameObject);
                    });
                });
            });
        }

        EffectBlock<Building> ChooseTarget => s => {
            var target = State.Create<Building>();
            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    Building bestTarget = null;
                    var bestSqr = float.MaxValue;
                    foreach (var building in Building.All) {
                        var sqr = (building.transform.position - transform.position).sqrMagnitude;
                        if (sqr < bestSqr) {
                            bestSqr = sqr;
                            bestTarget = building;
                        }
                    }
                    target.Set(bestTarget);
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
            return target;
        };

        EffectBlock Attack(Building target) => s => {
            IEnumerator onUpdate() {
                while (true) {
                    if (target == null) break;
                    var attackPos = target.transform.position;

                    while (true) {
                        var to = attackPos - transform.position;
                        to.y = 0f;
                        var dist = to.magnitude;
                        if (dist <= stopDistance + 0.001f) break;
                        var dir = to / dist;
                        var step = Mathf.Min(moveSpeed * Time.deltaTime, dist - stopDistance);
                        transform.position += dir * step;
                        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        yield return null;
                    }

                    // Wait for cooldown
                    yield return new WaitForSeconds(1f / fireRate);

                    // Launch bomb
                    Pool.Spawn(bombBlastPrefab, attackPos, Quaternion.identity);
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        EffectBlock RadarTrack => s => {
            var sensor = s.Use(GameState.RadarZone.AddSensor(() => new Circle(transform.position, radius)));

            var isTracked = s.Memo(s => sensor.Overlaps.Count > 0, sensor.OverlapsChanged);
            s.Phase(isTracked, s => {
                showOnTracked.SetActive(true);
                s.OnCleanup(() => showOnTracked.SetActive(false));

                s.Use(GameState.TrackedEnemyZone.AddCollider(this, () => new Circle(transform.position, radius)));
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

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}