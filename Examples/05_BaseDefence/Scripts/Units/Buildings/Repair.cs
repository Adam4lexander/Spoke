using System.Collections;
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
            hoverInfo.Set(new HoverInfo(
                $"{building.DisplayName.ToUpper()}\n\n" +
                "Repairs the most damaged building inside its coverage, one at a time.\n\n" +
                "Repair buildings can mend each other, but never themselves.",
                CoverageType.Repair, building.Power));

            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.gameObject.SetActive(false);

            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.Power.HasPower));

            s.Phase(isRunning, s => {
                s.Use(GameState.RepairZone.AddCollider(this, () => new Circle(transform.position, range)));

                var patient = s.Effect(FindPatient);
                s.Effect(s => {
                    var patientNow = s.D(patient);
                    if (patientNow != null) s.Effect(DoRepair(patientNow));
                });
            });
        }

        // Takes the most damaged building in range (excluding its own — repair towers can
        // cover each other, but not themselves). Idle while everyone's at full health.
        EffectBlock<Health> FindPatient => s => {
            var patient = State.Create<Health>();
            var sensor = s.Use(GameState.GroundZone.AddSensor(() => new Circle(transform.position, range)));

            s.Effect(s => {
                var patientNow = s.D(patient);
                if (patientNow == null) return;
                if (s.D(patientNow.HPFraction) >= 1f) {
                    patient.Set(null);
                    return;
                }
                foreach (var c in sensor.Overlaps) {
                    if (c.Owner == patientNow.gameObject) return;
                }
                patient.Set(null);
            }, sensor.OverlapsChanged);

            s.Effect(s => {
                if (s.D(patient) != null) return;
                Health best = null;
                var bestFrac = 1f;
                foreach (var c in sensor.Overlaps) {
                    if (c.Owner == building.gameObject) continue;
                    if (!c.Owner.TryGetComponent<Health>(out var health)) continue;
                    var frac = s.D(health.HPFraction);
                    if (frac < bestFrac) {
                        bestFrac = frac;
                        best = health;
                    }
                }
                patient.Set(best);
            }, sensor.OverlapsChanged);

            return patient;
        };

        // Heals the patient with the beam held on them, releasing them once they're healed
        // or gone (out of range or dead — either way their collider has left the sensor).
        EffectBlock DoRepair(Health patient) => s => {
            beam.SetPosition(0, fireFrom.transform.position);
            beam.SetPosition(1, patient.transform.position);
            beam.gameObject.SetActive(true);
            s.OnCleanup(() => beam.gameObject.SetActive(false));

            IEnumerator onUpdate() {
                while (true) {
                    yield return null;
                    patient.Repair(repairRate * Time.deltaTime);
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
