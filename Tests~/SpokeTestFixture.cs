using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Spoke.Tests {

    /// <summary>
    /// Base for every Spoke test fixture. Installs a capture over the global SpokeError.Log sink
    /// for the duration of each test, and fails the test in teardown if anything was logged that
    /// the test didn't explicitly opt into via Errors.ExpectErrors().
    /// This makes error-capture uniform (always on) rather than something each test remembers to add,
    /// and turns an unexpected runtime error-log into a test failure instead of silent console noise.
    /// </summary>
    public abstract class SpokeTestFixture {
        protected CapturedErrors Errors { get; private set; }

        [SetUp]
        public void __InstallErrorCapture() => Errors = new CapturedErrors();

        [TearDown]
        public void __AssertNoUnexpectedErrors() {
            try {
                if (!Errors.Expected) {
                    Assert.IsEmpty(Errors.Entries,
                        $"Test logged unexpected SpokeError.Log entries: {string.Join(" | ", Errors.Entries.Select(e => e.msg))}");
                }
            } finally {
                Errors.Dispose();
            }
        }
    }

    /// <summary>
    /// Captures entries written to the global SpokeError.Log sink, restoring the previous sink on dispose.
    /// Call ExpectErrors() from a test that intends to log — it opts the test out of the
    /// no-unexpected-errors teardown check (and marks the test, for the reader, as an error-path test).
    /// </summary>
    public sealed class CapturedErrors : IDisposable {
        readonly Action<string, Exception> previous;
        public List<(string msg, Exception ex)> Entries { get; } = new();
        public bool Expected { get; private set; }

        public CapturedErrors() {
            previous = SpokeError.Log;
            SpokeError.Log = (m, e) => Entries.Add((m, e));
        }

        public void ExpectErrors() => Expected = true;

        public void Dispose() => SpokeError.Log = previous;
    }
}
