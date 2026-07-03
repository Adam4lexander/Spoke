using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    public class Interface : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Text moneyText;
        [SerializeField] Text messageText;

        [Header("Attributes")]
        [SerializeField] UState<Color> powerCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> radarCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> turretCoverageColour = new(new Color(1f, 0.7372549f, 0f));
        [SerializeField] UState<Color> powerLinkColour = new(Color.cyan);
        [SerializeField] UState<Color> unitRingColour = new(Color.cyan);

        protected override void Init(EffectBuilder s) {
            s.Effect(s => moneyText.text = $"${s.D(GameState.Money)} (+{s.D(GameState.CollectRate)})");

            var hovered = s.Effect(TrackHovered);

            s.Effect(s => {
                var hoveredNow = s.D(hovered);
                var hoverable = hoveredNow?.Owner.GetComponent<IHoverable>();
                if (hoverable == null) { messageText.text = ""; return; }

                var description = s.Memo(s => s.D(hoverable.HoverInfo).Description);
                var coverage = s.Memo(s => s.D(hoverable.HoverInfo).Coverage);
                var powerNode = s.Memo(s => s.D(hoverable.HoverInfo).PowerNode);

                s.Effect(s => messageText.text = s.D(description));

                s.Effect(s => {
                    switch (s.D(coverage)) {
                        case CoverageType.Power: s.Effect(CoverageDisplay.Draw(GameState.PowerZone, powerCoverageColour, body => body.IsProvider)); break;
                        case CoverageType.Radar: s.Effect(CoverageDisplay.Draw(GameState.RadarZone, radarCoverageColour)); break;
                        case CoverageType.Turret: s.Effect(CoverageDisplay.Draw(GameState.TurretZone, turretCoverageColour)); break;
                    }
                });

                s.Effect(s => {
                    var node = s.D(powerNode);
                    if (node != null) s.Effect(LinkDisplay.Draw(node, powerLinkColour));
                });

                // Highlight ring, slightly larger than the unit's footprint.
                var circle = hoveredNow.Circle;
                s.Effect(CoverageDisplay.Draw(new Circle(circle.Center, circle.Radius * 1.5f), unitRingColour));
            });
        }

        // The ground unit currently under the mouse cursor (or null) — a point sensor that follows
        // the mouse across the ground plane. Overlaps are nearest-first, so [0] is the unit under
        // the cursor. Nothing is hovered while the cursor points at no ground (over UI, or its ray
        // misses the plane); the sensor holds its last point through those gaps.
        EffectBlock<ICollider<GameObject>> TrackHovered => s => {
            var point = default(Circle);
            Circle mousePoint() {
                var mp = GameState.View.Now.MousePoint;
                if (mp != null) point = new Circle(mp.Value, 0f);
                return point;
            }
            var sensor = s.Use(GameState.GroundZone.AddSensor(mousePoint));
            return s.Memo(s => s.D(GameState.View).MousePoint != null && sensor.Overlaps.Count > 0 ? sensor.Overlaps[0] : null, sensor.OverlapsChanged);
        };
    }
}
