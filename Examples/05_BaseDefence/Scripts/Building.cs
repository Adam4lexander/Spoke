using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public abstract class Building : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] float range;
        [SerializeField] float maxHp;

        public float Range => range;

        protected override void Init(EffectBuilder s) {
            
        }
    }
}