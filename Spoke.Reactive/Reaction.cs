namespace Spoke {

    /// <summary>
    /// An Effect that skips its first invocation, only running when a trigger explicitely fires
    /// </summary>
    public sealed class Reaction : BaseEffect {

        protected override bool AutoArmTickAfterInit => false;

        public Reaction(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            this.block = block;
        }
    }
}