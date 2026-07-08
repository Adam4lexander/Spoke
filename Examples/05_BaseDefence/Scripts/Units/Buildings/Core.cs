using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // The base's root building: the origin of the power grid, and the one whose destruction
    // ends the game.
    public class Core : SpokeBehaviour, IHoverable {

        [Header("Prefabs")]
        [SerializeField] GameObject coreExplodePrefab;

        [Header("References")]
        [SerializeField] Building building;
        [SerializeField] Health health;

        State<HoverInfo> hoverInfo = new();
        public ISignal<HoverInfo> HoverInfo => hoverInfo;

        protected override void Init(EffectBuilder s) {
            hoverInfo.Set(new HoverInfo(
                $"{building.DisplayName.ToUpper()}\n\n" +
                "Seeds the power grid, all buildings must trace a path to it for receiving power.\n\n" +
                "Game is over if this building is destroyed.",
                CoverageType.Power, building.Power));

            s.Phase(IsEnabled, s => {
                if (s.D(health.IsAlive)) return;

                // Once the Core dies, play its explosion, cleaned up when the phase unmounts.
                var explodeFx = Pool.Spawn(coreExplodePrefab, transform.position);
                s.OnCleanup(() => Pool.Despawn(explodeFx));
            });
        }
    }
}
