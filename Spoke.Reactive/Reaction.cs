using System;

namespace Spoke {

    /// <summary>
    /// An Effect that skips its first invocation, only running when a trigger explicitly fires
    /// </summary>
    public sealed class Reaction : Computation {
        readonly EffectBlock block;
        Action<ITrigger> _addDynamicTrigger;

        protected override bool AutoArmTickAfterInit => false;

        public Reaction(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = block;
            _addDynamicTrigger = AddDynamicTrigger;
        }

        protected override void OnRun(EpochBuilder s) {
            block?.Invoke(new EffectBuilder(_addDynamicTrigger, s));
        }
    }
}
