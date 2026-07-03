using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Turret : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;
        [SerializeField] GameObject pivot;
        [SerializeField] GameObject fireFrom;
        [SerializeField] LineRenderer beam;

        [Header("Attributes")]
        [SerializeField] float range;
        [SerializeField] float rotationSpeed;
        [SerializeField] float damage = 1f;
        [SerializeField] float fireRate = 2f;      // shots per second
        [SerializeField] float fireAngle = 5f;     // max muzzle-to-target angle (deg) allowed to fire
        [SerializeField] float beamFlashTime = 0.05f;

        Vector3 targetDirection = Vector3.zero;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo("Turret — fires at enemies revealed by radar", CoverageType.Turret, building.Power));

            targetDirection = fireFrom.transform.forward;

            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.gameObject.SetActive(false);

            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.Power.HasPower));

            s.Phase(isRunning, s => {
                s.Effect(RotateToTarget);

                s.Use(GameState.TurretZone.AddCollider(this, () => new Circle(transform.position, range)));

                var sensor = s.Use(GameState.TrackedEnemyZone.AddSensor(() => new Circle(transform.position, range)));
                var target = s.Memo(s => sensor.Overlaps.Count == 0 ? null : sensor.Overlaps[0].Owner, sensor.OverlapsChanged);

                s.Effect(s => {
                    var targetNow = s.D(target);
                    if (targetNow == null) s.Effect(IdleBehaviour);
                    else s.Effect(AttackBehaviour(targetNow));
                });
            });
        }

        EffectBlock RotateToTarget => s => {
            IEnumerator onUpdate() {
                while (true) {
                    var flat = Vector3.ProjectOnPlane(targetDirection, Vector3.up);
                    if (flat.sqrMagnitude > 0.0001f) {
                        var target = Quaternion.LookRotation(flat, Vector3.up);
                        pivot.transform.rotation = Quaternion.RotateTowards(pivot.transform.rotation, target, rotationSpeed * Time.deltaTime);
                    }
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        EffectBlock IdleBehaviour => s => {
            IEnumerator onUpdate() {
                const float minInterval = 1f;
                const float maxInterval = 3f;
                while (true) {
                    yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
                    var angle = Random.Range(0f, Mathf.PI * 2f);
                    targetDirection = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        EffectBlock AttackBehaviour(Enemy target) => s => {
            IEnumerator onUpdate() {
                var cooldown = 0f;
                while (true) {
                    if (!target) break;
                    var toTarget = target.transform.position - pivot.transform.position;
                    targetDirection = toTarget;

                    cooldown -= Time.deltaTime;
                    var muzzle = Vector3.ProjectOnPlane(fireFrom.transform.forward, Vector3.up);
                    var aim = Vector3.ProjectOnPlane(toTarget, Vector3.up);
                    if (cooldown <= 0f && Vector3.Angle(muzzle, aim) <= fireAngle) {
                        cooldown = 1f / fireRate;
                        // Flash the beam first, then land the hit — so the killing shot is seen
                        // before the enemy dies and we retarget.
                        beam.SetPosition(0, fireFrom.transform.position);
                        beam.SetPosition(1, target.transform.position);
                        beam.gameObject.SetActive(true);
                        yield return new WaitForSeconds(beamFlashTime);
                        beam.gameObject.SetActive(false);
                        if (target) target.Health.Damage(damage);
                    }
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => {
                StopCoroutine(routine);
                beam.gameObject.SetActive(false);
            });
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.red);
        }
    }
}