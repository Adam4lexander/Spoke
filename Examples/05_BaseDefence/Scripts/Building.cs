using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class Building : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] float radius = 0.6f;
        [SerializeField] float maxHp = 5;

        protected override void Init(EffectBuilder s) {
            
        }

        void OnDrawGizmosSelected() {
            var circle = new Circle(transform.position, radius);
            circle.DrawGizmo(Color.cyan);
        }
    }
}