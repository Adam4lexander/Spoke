using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public abstract class Building : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] UState<float> range = new(1);
        [SerializeField] float maxHp;

        public ISignal<float> Range => range;

        protected override void Init(EffectBuilder s) {
            
        }
    }
}