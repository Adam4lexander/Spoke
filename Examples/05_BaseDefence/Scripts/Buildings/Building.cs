using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] float maxHp = 5;
        [SerializeField] bool isCore = false;

        State<bool> hasService = new(false);
        public ISignal<bool> HasService => hasService;

        protected override void Init(EffectBuilder s) {
            s.Effect(ControlHasService);
        }

        EffectBlock ControlHasService => s => {
            if (isCore) {
                hasService.Set(true);
                return;
            }
        };

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}