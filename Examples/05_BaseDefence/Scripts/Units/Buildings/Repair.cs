using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Repair : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;
        [SerializeField] GameObject fireFrom;
        [SerializeField] LineRenderer beam;

        [Header("Attributes")]
        [SerializeField] float range;
        [SerializeField] float repairRate = 0.5f;   // HP per second

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo("Repair — mends damaged buildings in range", CoverageType.Repair, building.Power));

            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.gameObject.SetActive(false);

            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.Power.HasPower));

            s.Phase(isRunning, s => {
                s.Use(GameState.RepairZone.AddCollider(this, () => new Circle(transform.position, range)));

                var sensor = s.Use(GameState.GroundZone.AddSensor(() => new Circle(transform.position, range)));

                var patient = State.Create<Health>();
                var hasPatient = s.Memo(s => s.D(patient) != null);
                var noPatient = s.Memo(s => !s.D(hasPatient));

                s.Phase(noPatient, FindPatient(sensor, patient));
                s.Phase(hasPatient, RepairPatient(sensor, patient));
            });
        }

        // Takes the most damaged building in range (excluding its own — repair towers can
        // cover each other, but not themselves). Idle while everyone's at full health.
        EffectBlock FindPatient(ISensor<GameObject> sensor, State<Health> patient) => s => {
            s.Effect(s => {
                Health best = null;
                var bestFrac = 1f;
                foreach (var c in sensor.Overlaps) {
                    if (c.Owner == building.gameObject) continue;
                    var health = c.Owner.GetComponent<Health>();
                    if (health == null) continue;
                    var frac = s.D(health.HPFraction);
                    if (frac < bestFrac) {
                        bestFrac = frac;
                        best = health;
                    }
                }
                if (best != null) patient.Set(best);
            }, sensor.OverlapsChanged);
        };

        // Heals the patient with the beam held on them, releasing them once they're healed
        // or gone (out of range or dead — either way their collider has left the sensor).
        EffectBlock RepairPatient(ISensor<GameObject> sensor, State<Health> patient) => s => {
            var target = patient.Now;

            beam.SetPosition(0, fireFrom.transform.position);
            beam.SetPosition(1, target.transform.position);
            beam.gameObject.SetActive(true);
            s.OnCleanup(() => beam.gameObject.SetActive(false));

            s.Effect(s => {
                var inRange = false;
                foreach (var c in sensor.Overlaps) {
                    if (c.Owner == target.gameObject) { inRange = true; break; }
                }
                if (!inRange || s.D(target.HPFraction) >= 1f) patient.Set(null);
            }, sensor.OverlapsChanged);

            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    if (target) target.Repair(repairRate * Time.deltaTime);
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.green);
        }
    }
}
