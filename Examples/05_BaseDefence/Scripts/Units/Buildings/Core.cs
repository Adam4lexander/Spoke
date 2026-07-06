using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // The base's root building — the origin of the power network.
    public class Core : SpokeBehaviour, IHoverable {

        [Header("References")]
        [SerializeField] Building building;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo(
                $"{building.DisplayName.ToUpper()}\n\n" +
                "Seeds the power grid, all buildings must trace a path to it for receiving power.\n\n" +
                "Game is over if this building is destroyed.",
                CoverageType.Power, building.Power));

            // The Core only despawns when destroyed (after its shatter plays out),
            // so going disabled is the game-over signal. The guard keeps a Core
            // torn down after the game has already ended from rewriting the mode.
            s.Phase(IsEnabled, s => {
                s.OnCleanup(() => {
                    if (GameState.Mode.Now == GameMode.Playing) GameState.Mode.Set(GameMode.GameOver);
                });
            });
        }
    }
}
