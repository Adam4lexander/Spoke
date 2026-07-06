using System;
using System.Collections;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Enemy : SpokeBehaviour {

        [Header("Prefabs")]
        [SerializeField] GameObject bombBlastPrefab;

        [Header("References")]
        [SerializeField] Health health;
        [SerializeField] HealthBar healthBar;
        [SerializeField] MeshFX meshFX;
        [SerializeField] GameObject flightRoot;
        [SerializeField] GameObject fireFrom;
        [SerializeField] GameObject showOnTracked;

        [Header("Attributes")]
        [SerializeField] float radius;
        [SerializeField] float moveSpeed = 2f;
        [SerializeField] float stopDistance = 1.2f;   // gap kept from the target building's centre
        [SerializeField] float fireRate = 1f;         // shots per second
        [SerializeField] float proximityBias = 0.5f;  // 0 = follow the heading line, higher favours nearby buildings
        [SerializeField] float separationDistance = 1.5f; // enemies closer than this push apart
        [SerializeField] float separationStrength = 2f;   // how hard overlapping enemies repel

        public Health Health => health;
        public Vector3 CenterOfMass => flightRoot.transform.position;

        State<bool> tracked = new();
        public ISignal<bool> IsTracked => tracked;

        Vector3 flightRootStartPos;

        protected override void Init(EffectBuilder s) {
            flightRootStartPos = flightRoot.transform.localPosition;
            showOnTracked.SetActive(false);

            s.Effect(s => {
                var showHealth = s.D(health.IsAlive) && s.D(health.HPFraction) < 1f;
                healthBar.gameObject.SetActive(showHealth);
                healthBar.Fraction.Set(s.D(health.HPFraction));
            });

            s.Phase(IsEnabled, s => {
                s.Phase(health.IsAlive, s => {
                    s.Use(GameState.EnemyZone.AddCollider(this, () => new Circle(transform.position, radius)));

                    s.Effect(RadarTrack);
                    s.Effect(Bob);
                    s.Effect(Separate);
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

        // Each life picks a random heading across the base, and targets the building that
        // best blends "in my path" with "near me" — so enemies cut lines through the base
        // instead of all piling onto the nearest building. Once everything lies behind the
        // heading, the score reduces to plain nearest-building.
        EffectBlock<Building> ChooseTarget => s => {
            var target = State.Create<Building>();

            // Aim at the central half of the level, so the line always crosses the base interior.
            var bounds = GameState.LevelBounds;
            var aim = bounds.center + 0.5f * new Vector3(
                UnityEngine.Random.Range(-bounds.extents.x, bounds.extents.x),
                0f,
                UnityEngine.Random.Range(-bounds.extents.z, bounds.extents.z));
            var heading = aim - transform.position;
            heading.y = 0f;
            heading.Normalize();

            s.Coroutine(() => {
                Building bestTarget = null;
                var bestScore = float.MaxValue;
                foreach (var building in Building.All) {
                    var v = building.transform.position - transform.position;
                    v.y = 0f;
                    var along = Mathf.Max(0f, Vector3.Dot(v, heading));
                    var offLine = (v - along * heading).magnitude;
                    var score = offLine + proximityBias * v.magnitude;
                    if (score < bestScore) {
                        bestScore = score;
                        bestTarget = building;
                    }
                }
                target.Set(bestTarget);
            });
            return target;
        };

        EffectBlock Attack(Building target) => s => {
            IEnumerator onUpdate() {
                while (true) {
                    if (target == null) break;
                    var attackPos = target.transform.position;

                    // Yaw lives on the flight root, so children of the enemy root (like the
                    // health bar) never inherit rotation.
                    while (true) {
                        var to = attackPos - transform.position;
                        to.y = 0f;
                        var dist = to.magnitude;
                        if (dist > 0.001f) flightRoot.transform.rotation = Quaternion.LookRotation(to / dist, Vector3.up);
                        if (dist <= stopDistance + 0.001f) break;
                        var step = Mathf.Min(moveSpeed * Time.deltaTime, dist - stopDistance);
                        transform.position += to / dist * step;
                        yield return null;
                    }

                    // Wait for cooldown
                    yield return new WaitForSeconds(1f / fireRate);

                    // Launch bomb
                    Pool.Spawn(bombBlastPrefab, attackPos, Quaternion.identity);
                    yield return null;
                }
            }
            
            s.Coroutine(onUpdate());
        };

        // A gentle repulsion between living enemies, so they spread out instead of
        // stacking when several converge on the same building.
        EffectBlock Separate => s => {
            var sensor = s.Use(GameState.EnemyZone.AddSensor(() => new Circle(transform.position, separationDistance)));

            s.Coroutine(() => {
                var push = Vector3.zero;
                foreach (var c in sensor.Overlaps) {
                    if (c.Owner == this) continue;
                    var away = transform.position - c.Owner.transform.position;
                    away.y = 0f;
                    var dist = away.magnitude;
                    if (dist < 0.001f || dist >= separationDistance) continue;
                    // Full strength when stacked, fading to zero at the separation distance.
                    push += away / dist * (1f - dist / separationDistance);
                }
                transform.position += separationStrength * Time.deltaTime * push;
            });
        };

        EffectBlock RadarTrack => s => {
            var sensor = s.Use(GameState.RadarZone.AddSensor(() => new Circle(transform.position, radius)));

            var isTracked = s.Memo(s => sensor.Overlaps.Count > 0, sensor.OverlapsChanged);
            s.Phase(isTracked, s => {
                tracked.Set(true);
                s.OnCleanup(() => tracked.Set(false));

                showOnTracked.SetActive(true);
                s.OnCleanup(() => showOnTracked.SetActive(false));
            });
        };

        EffectBlock Bob => s => {
            const float bobAmplitude = 0.05f;
            const float bobSpeed = 6f;
            var phase = UnityEngine.Random.value * Mathf.PI * 2f;   // desync enemies so they don't bob in lockstep
            s.Coroutine(() => {
                var p = flightRootStartPos + Vector3.up * Mathf.Sin(Time.time * bobSpeed + phase) * bobAmplitude;
                flightRoot.transform.localPosition = p;
            });
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.red);
        }
    }
}