using System.Collections.Generic;
using System;

namespace Spoke {

    public struct SpokePool<T> where T : new() {
        Stack<T> pool; 
        Action<T> reset;

        public static SpokePool<T> Create(Action<T> reset = null) {
            return new SpokePool<T> { 
                pool = new Stack<T>(), 
                reset = reset 
            };
        }

        public T Get() {
            if (pool.Count > 0) return pool.Pop();
            return new T();
        }
        
        public void Return(T o) { 
            reset?.Invoke(o); 
            pool.Push(o); 
        }
    }
}