using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Spoke.Examples.BaseDefence {

    public class Health : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] UState<float> maxHP = new(1f);

        [Header("Runtime State (Dont Edit)")]
        [SerializeField] UState<float> damage = new(0f);
        [SerializeField] UState<float> hpFrac = new(1f);
        [SerializeField] UState<bool> isAlive = new(true);

        public ISignal<float> HPFraction => hpFrac;
        public ISignal<bool> IsAlive => isAlive;

        Trigger damaged = Trigger.Create();
        public ITrigger Damaged => damaged;

        public void Damage(float amount) {
            damage.Update(x => x + amount);
            damaged.Invoke();
        }

        protected override void Init(EffectBuilder s) {

            var hp = s.Memo(s => s.D(maxHP) - s.D(damage));

            s.Effect(s => {
                hpFrac.Set(s.D(hp) / s.D(maxHP));
            });

            s.Effect(s => {
                isAlive.Set(s.D(hp) > 0f);
            });
        }
    }
}