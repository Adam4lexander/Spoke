using System.Collections.Generic;
using System;

namespace Spoke {

    // TODO: Remove strong refs to Epoch instances. Take data snapshot, enough for toString() of
    // stack trace, and a weakref to the epoch.
    public sealed class SpokeException : Exception {
        List<SpokeRuntime.Frame> stackSnapshot = new List<SpokeRuntime.Frame>();
        string innerTrace;
        public bool SkipMarkFaulted;

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