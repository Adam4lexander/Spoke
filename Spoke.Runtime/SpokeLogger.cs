using System;

namespace Spoke {

    public interface ISpokeLogger {
        void Log(string message);
        void Error(string message);
    }

    public class ConsoleSpokeLogger : ISpokeLogger {
        public void Log(string msg) {
            Console.WriteLine(msg);
        }

        public void Error(string msg) {
            Console.WriteLine(msg);
        }
    }

    public static class SpokeError {
        internal static Action<string, Exception> Log 
            = (msg, ex) => Console.WriteLine($"[Spoke] {msg}\n{ex}");
        
        internal static ISpokeLogger DefaultLogger = new ConsoleSpokeLogger();
    }
}