using System;

namespace Spoke {

    /// <summary>
    /// A Phase is a specialized Effect which only runs its block when mountWhen is true
    /// </summary>
    public sealed class Phase : Computation {
        readonly EffectBlock block;
        ISignal<bool> mountWhen;
        Action<ITrigger> _addDynamicTrigger;

        public Phase(string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.mountWhen = mountWhen;
            this.block = block;
            _addDynamicTrigger = AddDynamicTrigger;
        }

        protected override TickBlock Init(EpochBuilder s) {
            var mountBlock = base.Init(s);
            AddStaticTrigger(mountWhen);
            return mountBlock;
        }

        protected override void OnRun(EpochBuilder s) {
            if (mountWhen.Now) {
                block?.Invoke(new EffectBuilder(_addDynamicTrigger, s));
            }
        }
    }
}
