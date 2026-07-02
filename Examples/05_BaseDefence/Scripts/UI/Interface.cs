using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples.BaseDefence {

    public class Interface : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Text moneyText;

        protected override void Init(EffectBuilder s) {
            s.Effect(s => moneyText.text = $"${s.D(GameState.Money)} (+{s.D(GameState.CollectRate)})");
        }
    }
}