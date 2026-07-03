using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    public class Interface : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Text moneyText;
        [SerializeField] Text messageText;

        protected override void Init(EffectBuilder s) {
            s.Effect(s => moneyText.text = $"${s.D(GameState.Money)} (+{s.D(GameState.CollectRate)})");

            s.Effect(s => {
                var hoveredNow = s.D(GameState.Hovered);
                messageText.text = Describe(hoveredNow?.Owner);
            });
        }

        static string Describe(GameObject go) {
            if (go == null) return "";
            var power = go.GetComponent<PowerNode>();
            if (power != null && !power.IsLeaf) return "Power node — relays power to nearby buildings";
            if (go.GetComponent<Radar>() != null) return "Radar — reveals enemies to nearby turrets";
            if (go.GetComponent<Turret>() != null) return "Turret — fires at enemies revealed by radar";
            if (go.GetComponent<Resource>() != null) return "Resource — generates money while powered";
            return "";
        }
    }
}