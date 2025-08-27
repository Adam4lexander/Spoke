using System;
using System.Collections.Generic;

namespace Spoke {

    public sealed class Dock : Epoch, Dock.Introspect {

        new internal interface Introspect { 
            List<Epoch> GetChildren(List<Epoch> storeIn = null); 
        }
        
        Dictionary<object, Epoch> dynamicChildren = new();
        bool isDetaching;
        long childIndex;

        public Dock() { 
            Name = "Dock"; 
        }

        public Dock(string name) { 
            Name = name; 
        }

        public T Call<T>(object key, T epoch) where T : Epoch {
            if (isDetaching) {
                throw new Exception("Cannot Call while detaching");
            }
            (SpokeRuntime.Local as SpokeRuntime.Friend).Push(new(SpokeRuntime.FrameKind.Dock, this));
            Drop(key);
            dynamicChildren.Add(key, epoch);
            var childCoords = Coords.Extend(childIndex++);
            var childTicker = (this as Epoch.Friend).GetTicker();
            (epoch as Epoch.Friend).Attach(this, childCoords, childTicker, null);
            (SpokeRuntime.Local as SpokeRuntime.Friend).Pop();
            return epoch;
        }

        public void Drop(object key) {
            if (!dynamicChildren.TryGetValue(key, out var child)) return;
            (child as Epoch.Friend).Detach();
            dynamicChildren.Remove(key);
        }

        protected override TickBlock Init(EpochBuilder s) {
            s.OnCleanup(() => {
                isDetaching = true;
                var children = (this as Introspect).GetChildren();
                for (int i = children.Count - 1; i >= 0; i--) {
                    (children[i] as Epoch.Friend).Detach();
                }
                dynamicChildren.Clear();
            });
            return null;
        }

        List<Epoch> Introspect.GetChildren(List<Epoch> storeIn) {
            storeIn = storeIn ?? new List<Epoch>();
            foreach (var child in dynamicChildren) {
                storeIn.Add(child.Value);
            }
            storeIn.Sort((a, b) => a.Coords.CompareTo(b.Coords));
            return storeIn;
        }
    }
}