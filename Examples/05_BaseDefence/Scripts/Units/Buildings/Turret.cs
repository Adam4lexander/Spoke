using System.Collections;
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
        public Circle Footprint => building.Footprint;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo(
                $"{building.DisplayName.ToUpper()}\n\n" +
                "Fires at enemies inside its coverage that radar has revealed.\n\n" +
                "Pair with radar coverage — a turret alone sees nothing.",
                CoverageType.Turret, building.Power));

            targetDirection = fireFrom.transform.forward;

            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.gameObject.SetActive(false);

            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.Power.HasPower));

            s.Phase(isRunning, s => {
                s.Effect(RotateToTarget);

                s.Use(GameState.TurretZone.AddCollider(this, () => new Circle(transform.position, range)));

                // Overlaps are nearest-first, so this picks the closest radar-revealed enemy.
                var sensor = s.Use(GameState.EnemyZone.AddSensor(() => new Circle(transform.position, range)));
                var target = s.Memo(s => {
                    foreach (var c in sensor.Overlaps)
                        if (s.D(c.Owner.IsTracked)) return c.Owner;
                    return null;
                }, sensor.OverlapsChanged);

                s.Effect(s => {
                    var targetNow = s.D(target);
                    if (targetNow == null) s.Effect(IdleBehaviour);
                    else s.Effect(AttackBehaviour(targetNow));
                });
            });
        }

        EffectBlock RotateToTarget => s => {
            s.Coroutine(() => {
                var flat = Vector3.ProjectOnPlane(targetDirection, Vector3.up);
                if (flat.sqrMagnitude > 0.0001f) {
                    var target = Quaternion.LookRotation(flat, Vector3.up);
                    pivot.transform.rotation = Quaternion.RotateTowards(pivot.transform.rotation, target, rotationSpeed * Time.deltaTime);
                }
            });
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
            s.Coroutine(onUpdate());
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
                        beam.SetPosition(1, target.CenterOfMass);
                        beam.gameObject.SetActive(true);
                        yield return new WaitForSeconds(beamFlashTime);
                        beam.gameObject.SetActive(false);
                        if (target) target.Health.Damage(damage);
                    }
                    yield return null;
                }
            }
            s.Coroutine(onUpdate());
            s.OnCleanup(() => beam.gameObject.SetActive(false));
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.red);
        }
    }
}