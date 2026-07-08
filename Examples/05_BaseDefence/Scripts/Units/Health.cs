using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    /// <summary>Basic health system used by buildings and enemies.</summary>
    public class Health : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] UState<float> maxHP = new(1f);
        [SerializeField] UState<float> damage = new(0f);

        State<float> hpFrac = new(1f);
        State<bool> isAlive = new(true);
        Trigger damaged = Trigger.Create();

        /// <summary>Current HP as a fraction of max, from 1 down to 0.</summary>
        public ISignal<float> HPFraction => hpFrac;

        /// <summary>True while HP is above zero.</summary>
        public ISignal<bool> IsAlive => isAlive;

        /// <summary>Fires each time the unit takes damage.</summary>
        public ITrigger Damaged => damaged;

        public void Damage(float amount) {
            damage.Update(x => x + amount);
            damaged.Invoke();
        }

        public void Repair(float amount) {
            if (!isAlive.Now) return;
            damage.Update(x => Mathf.Max(0f, x - amount));
        }

        protected override void Init(EffectBuilder s) {
            var hp = s.Memo(s => s.D(maxHP) - s.D(damage));

            s.Effect(s => {
                hpFrac.Set(s.D(hp) / s.D(maxHP));
                isAlive.Set(s.D(hp) > 0f);
            });

            s.Phase(IsEnabled, s => s.OnCleanup(() => damage.Set(0f)));   // restore full health on return to the pool
        }
    }
}