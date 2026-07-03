using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    public class Interface : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Text moneyText;
        [SerializeField] Text messageText;
        [SerializeField] Overlays overlays;

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
                        case CoverageType.Power: s.Effect(overlays.PowerCoverage); break;
                        case CoverageType.Radar: s.Effect(overlays.RadarCoverage); break;
                        case CoverageType.Turret: s.Effect(overlays.TurretCoverage); break;
                    }
                });

                s.Effect(s => {
                    var node = s.D(powerNode);
                    if (node != null) s.Effect(overlays.PowerLinks(node));
                });

                s.Effect(overlays.UnitRing(hoveredNow));
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
