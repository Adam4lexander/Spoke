namespace Spoke {

    /// <summary>
    /// Define epochs in a functional composition style.
    /// Instead of subclassing Epoch, and overriding Init, you can use a LambdaEpoch.
    /// The InitBlock is a delegate that matches the signature of Epoch.Init.
    /// </summary>
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