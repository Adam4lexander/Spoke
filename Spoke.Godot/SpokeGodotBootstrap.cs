using System.Runtime.CompilerServices;
using Godot;

namespace Spoke {

    /// <summary>
    /// Static bootstrap for Spoke in Godot. Routes Spoke's internal error logging to GD.PushError,
    /// so faults land in the editor's Debugger panel instead of scrolling past in Output.
    ///
    /// Godot has no equivalent of Unity's [RuntimeInitializeOnLoadMethod], but it doesn't need one:
    /// [ModuleInitializer] runs once when the assembly loads, before any script executes. That covers
    /// the running game, [Tool] scripts in the editor, and post-build assembly reloads alike, so
    /// there is nothing to wire up and no autoload to register.
    /// </summary>
    public static class SpokeGodotBootstrap {

        static bool isInitialized;

        // CA2255 warns that [ModuleInitializer] is meant for application code, not libraries. Spoke
        // ships as source and compiles into the game assembly, so this *is* application code — and
        // an initializer that runs before user scripts is exactly the point. Suppressed rather than
        // inflicted on every consumer's build log.
#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void ModuleInit()
            => EnsureInitialized();

        public static void EnsureInitialized() {
            if (isInitialized) return;
            isInitialized = true;
            SpokeError.Log = (msg, ex) => GD.PushError($"[Spoke] {msg}\n{ex}");
            SpokeError.DefaultLogger = new GodotContext();
        }
    }
}
