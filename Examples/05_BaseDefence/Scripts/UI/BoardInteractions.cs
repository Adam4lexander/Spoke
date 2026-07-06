using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    // The player's interaction with the game board: hover and placement,
    // with the coverage and link displays that support them.
    public class BoardInteractions : SpokeBehaviour {

        [Header("Attributes")]
        [SerializeField] UState<Color> powerCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> radarCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> turretCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> repairCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> powerLinkColour = new(Color.cyan);
        [SerializeField] UState<Color> unitRingColour = new(Color.cyan);
        [SerializeField] UState<Color> validPlacementColour = new(Color.green);
        [SerializeField] UState<Color> invalidPlacementColour = new(Color.red);

        public State<BuildItem> Placing { get; } = new();

        // The unit under the mouse; null when nothing hoverable is there.
        State<IHoverable> hovering = new();
        public ISignal<IHoverable> Hovering => hovering;

        protected override void Init(EffectBuilder s) {
            var isPlaying = s.Memo(s => s.D(GameState.Mode) == GameMode.Playing);

            s.Phase(isPlaying, s => {
                s.OnCleanup(() => Placing.Set(null));

                var hasMousePoint = s.Memo(s => {
                    var p = s.D(GameState.View).MousePoint;
                    return p != null && GameState.LevelBounds.Contains(p.Value);
                });

                // The grid's whole spanning tree — shown unless a single unit's chain is in
                // focus, or a placement is in progress.
                s.Effect(s => {
                    if (s.D(hovering) != null || s.D(Placing) != null) return;
                    s.Effect(LinkDisplay.DrawAll(powerLinkColour));
                });

                s.Effect(ShowHovered);

                s.Phase(hasMousePoint, s => {
                    var placingNow = s.D(Placing);
                    if (placingNow == null) s.Effect(FindHovered);
                    else s.Effect(PlaceBuilding(placingNow));
                });
            });
        }

        // Publishes the unit under the mouse into the hovering state.
        EffectBlock FindHovered => s => {
            var sensor = s.Use(GameState.GroundZone.AddSensor(() => new Circle(GameState.View.Now.MousePoint.Value, 0f)));
            s.OnCleanup(() => hovering.Set(null));
            s.Effect(s => {
                var overlap = sensor.Overlaps.Count > 0 ? sensor.Overlaps[0] : null;
                hovering.Set(overlap?.Owner.GetComponent<IHoverable>());
            }, sensor.OverlapsChanged);
        };

        EffectBlock ShowHovered => s => {
            var hoverable = s.D(hovering);
            if (hoverable == null) return;

            var coverage = s.Memo(s => s.D(hoverable.HoverInfo).Coverage);
            s.Effect(s => s.Effect(ShowCoverage(s.D(coverage))));

            var powerNode = s.Memo(s => s.D(hoverable.HoverInfo).PowerNode);
            s.Effect(s => {
                var node = s.D(powerNode);
                if (node != null) s.Effect(LinkDisplay.Draw(node, powerLinkColour));
            });

            // Highlight ring, slightly larger than the unit's footprint.
            var circle = hoverable.Footprint;
            s.Effect(CoverageDisplay.Draw(new Circle(circle.Center, circle.Radius * 1.5f), unitRingColour));
        };

        // The placement experience: power coverage and the placed type's own coverage show while
        // choosing a spot, and the building's footprint follows the mouse — recoloured by whether
        // it can go there (touching provider coverage, clear of other units). A click on a valid
        // spot buys and places the building.
        EffectBlock PlaceBuilding(BuildItem item) => s => {
            var prefab = item.Prefab;
            s.Effect(CoverageDisplay.Draw(GameState.PowerZone, powerCoverageColour, body => body.IsProvider));
            if (item.Coverage != CoverageType.Power) s.Effect(ShowCoverage(item.Coverage));

            var mousePos = s.Memo(s => s.D(GameState.View).MousePoint.Value);
            var footprint = s.Memo(s => new Circle(s.D(mousePos), prefab.Radius));

            var groundSensor = s.Use(GameState.GroundZone.AddSensor(() => footprint.Now));
            var powerSensor = s.Use(GameState.PowerZone.AddSensor(() => new Circle(mousePos.Now, 0f), body => body.IsProvider));
            var isValid = s.Memo(s => groundSensor.Overlaps.Count == 0 && powerSensor.Overlaps.Count > 0,
                groundSensor.OverlapsChanged, powerSensor.OverlapsChanged);
            var colour = s.Memo(s => s.D(isValid) ? s.D(validPlacementColour) : s.D(invalidPlacementColour));
            
            s.Effect(CoverageDisplay.Draw(footprint, colour));

            s.Subscribe(InputSignals.LeftClick, () => {
                if (!isValid.Now) return;
                Pool.Spawn(prefab.gameObject, new Vector3(mousePos.Now.x, prefab.transform.position.y, mousePos.Now.z), Quaternion.identity);
                GameState.Money.Update(x => x - prefab.Cost);
                Placing.Set(null);
            });

            s.Subscribe(InputSignals.RightClick, () => Placing.Set(null));
        };

        // One coverage type → its zone drawn in its palette colour.
        EffectBlock ShowCoverage(CoverageType type) => s => {
            switch (type) {
                case CoverageType.Power: s.Effect(CoverageDisplay.Draw(GameState.PowerZone, powerCoverageColour, body => body.IsProvider)); break;
                case CoverageType.Radar: s.Effect(CoverageDisplay.Draw(GameState.RadarZone, radarCoverageColour)); break;
                case CoverageType.Turret: s.Effect(CoverageDisplay.Draw(GameState.TurretZone, turretCoverageColour)); break;
                case CoverageType.Repair: s.Effect(CoverageDisplay.Draw(GameState.RepairZone, repairCoverageColour)); break;
            }
        };
    }
}
