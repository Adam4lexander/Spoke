namespace Spoke {

    public sealed class Reaction : BaseEffect {

        public Reaction(string name, EffectBlock block, params ITrigger[] triggers) : base(name, triggers) {
            var isFirst = true;
            this.block = s => {
                if (!isFirst) {
                    block?.Invoke(s);
                } else {
                    isFirst = false;
                }
            };
        }
    }
}