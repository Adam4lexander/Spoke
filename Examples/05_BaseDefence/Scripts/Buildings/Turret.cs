using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Turret : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Building building;
        [SerializeField] GameObject pivot;
        [SerializeField] GameObject fireFrom;

        [Header("Attributes")]
        [SerializeField] float range;
        [SerializeField] float rotationSpeed;

        Vector3 targetDirection = Vector3.zero;

        protected override void Init(EffectBuilder s) {
            targetDirection = fireFrom.transform.forward;

            var isRunning = s.Memo(s => s.D(IsEnabled) && s.D(building.HasService));

            s.Phase(isRunning, s => {
                s.Effect(RotateToTarget);
                var target = s.Effect(ChooseEnemy);
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

        EffectBlock<Enemy> ChooseEnemy => s => {
            var watch = GameState.Instance.TrackedEnemyZone.Watch(new Circle(s.D(building.Position), range));
            return s.Memo(s => {
                var itemsNow = s.D(watch.Items);
                if (itemsNow.Count == 0) return null;
                return itemsNow[0].RefObject;
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
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        EffectBlock AttackBehaviour(Enemy target) => s => {
            IEnumerator onUpdate() {
                while (true) {
                    if (!target) break;
                    targetDirection = target.transform.position - pivot.transform.position;
                    yield return null;
                }
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => StopCoroutine(routine));
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, range);
            circle.DrawGizmo(Color.red);
        }
    }
}