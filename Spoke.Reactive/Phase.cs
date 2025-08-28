namespace Spoke {

    /// <summary>
    /// A Phase is a specialized Effect which only runs its block when mountWhen is true
    /// </summary>
    public sealed class Phase : BaseEffect {
        ISignal<bool> mountWhen;

        public Phase(string name, ISignal<bool> mountWhen, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.mountWhen = mountWhen;
            this.block = s => {
                if (mountWhen.Now) {
                    block?.Invoke(s);
                }
            };
        }

        protected override TickBlock Init(EpochBuilder s) {
            var mountBlock = base.Init(s);
            AddStaticTrigger(mountWhen);
            return mountBlock;
        }
    }
}