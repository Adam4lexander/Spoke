using System.Collections.Generic;
using System;

namespace Spoke {

    public struct SpokeHandle : IDisposable, IEquatable<SpokeHandle> {
        long id; 
        Action<long> onDispose;

        public static SpokeHandle Of(long id, Action<long> onDispose) {
            return new SpokeHandle { 
                id = id, 
                onDispose = onDispose 
            };
        }

        public void Dispose() {
            onDispose?.Invoke(id);
        }

        public bool Equals(SpokeHandle other) {
            return id == other.id && onDispose == other.onDispose;
        }

        public override bool Equals(object obj) { 
            return obj is SpokeHandle other && Equals(other); 
        }

        public override int GetHashCode() {
            int hashCode = -1348004479;
            hashCode = hashCode * -1521134295 + id.GetHashCode();
            return hashCode * -1521134295 + EqualityComparer<Action<long>>.Default.GetHashCode(onDispose);
        }

        public static bool operator ==(SpokeHandle left, SpokeHandle right) {
            return left.Equals(right);
        }

        public static bool operator !=(SpokeHandle left, SpokeHandle right) {
            return !left.Equals(right);
        }
    }
}