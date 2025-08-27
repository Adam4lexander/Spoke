using System.Collections.Generic;
using System;

namespace Spoke {

    /// <summary>
    /// A pool of reusable objects, for avoiding GC.
    /// </summary>
    public struct SpokePool<T> where T : new() {
        Stack<T> pool; 
        Action<T> reset; // Optional reset action, called when an object is returned to the pool.

        public static SpokePool<T> Create(Action<T> reset = null) {
            return new SpokePool<T> { 
                pool = new Stack<T>(), 
                reset = reset 
            };
        }

        /// <summary>
        /// Get an object from the pool, or create a new one if the pool is empty.
        /// </summary>
        public T Get() {
            if (pool.Count > 0) return pool.Pop();
            return new T();
        }
        
        /// <summary>
        /// Return an object to the pool, invoking the reset action first if provided.
        /// </summary>
        public void Return(T o) { 
            reset?.Invoke(o); 
            pool.Push(o); 
        }
    }
}