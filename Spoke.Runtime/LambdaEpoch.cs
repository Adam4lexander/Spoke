namespace Spoke {

    public sealed class LambdaEpoch : Epoch {
        InitBlock block;

        public LambdaEpoch(string name, InitBlock block) { 
            Name = name; 
            this.block = block; 
        }

        public LambdaEpoch(InitBlock block) : this("LambdaEpoch", block) { }

        protected override TickBlock Init(EpochBuilder s) {
            return block(s);
        }
    }
}