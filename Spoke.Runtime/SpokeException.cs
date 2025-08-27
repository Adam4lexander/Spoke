using System.Collections.Generic;
using System;

namespace Spoke {

    /// <summary>
    /// When an uncaught exception is thrown in the tree, its wrapped in a SpokeException and bubbles
    /// up the chain of tickers, marking each as faulted on the way.
    /// Creates a snapshot of Spokes virtual call stack, for debugging.
    /// </summary>
    public sealed class SpokeException : Exception {
        // TODO: Remove strong refs to Epoch instances. Take data snapshot, enough for toString() of
        // stack trace, and a weakref to the epoch.
        List<SpokeRuntime.Frame> stackSnapshot = new List<SpokeRuntime.Frame>();
        string innerTrace;
        public bool SkipMarkFaulted; // Epochs may toggle this and rethrow, to avoid marking themselves as faulted.

        public ReadOnlyList<SpokeRuntime.Frame> StackSnapshot => new(stackSnapshot);

        internal SpokeException(string msg, Exception inner) : base(msg, inner) {
            foreach (var frame in SpokeRuntime.Frames) {
                stackSnapshot.Add(frame);
            }
            innerTrace = inner.ToString();
        }

        public override string ToString() {
            return $"{SpokeIntrospect.TreeTrace(StackSnapshot)}\n{innerTrace}";
        }
    }
}