using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>Basic health system used by buildings, resource sites and enemies.</summary>
public partial class Health : SpokeNode {

    /// <summary>The unit this component belongs to. Godot's answer to Unity's gameObject.</summary>
    public Node2D Unit => (Node2D)Owner;

    [Export] public float MaxHp { get => maxHp.Now; set => maxHp.Set(value); }

    readonly State<float> maxHp = State.Create(1f);
    readonly State<float> damage = State.Create(0f);
    readonly State<float> hpFrac = State.Create(1f);
    readonly State<bool> isAlive = State.Create(true);
    readonly Trigger damaged = Trigger.Create();

    /// <summary>Current HP as a fraction of max, from 1 down to 0.</summary>
    public ISignal<float> HPFraction => hpFrac;

    /// <summary>True while HP is above zero.</summary>
    public ISignal<bool> IsAlive => isAlive;

    /// <summary>Fires each time the unit takes damage.</summary>
    public ITrigger Damaged => damaged;

    public void Damage(float amount) {
        damage.Update(x => x + amount);
        damaged.Invoke();
    }

    public void Repair(float amount) {
        if (!isAlive.Now) return;
        damage.Update(x => Mathf.Max(0f, x - amount));
    }

    protected override void Init(EffectBuilder s) {
        var hp = s.Memo(s => s.D(maxHp) - s.D(damage));

        s.Effect(s => {
            hpFrac.Set(s.D(hp) / s.D(maxHp));
            isAlive.Set(s.D(hp) > 0f);
        });

        s.Phase(IsInTree, s => s.OnCleanup(() => damage.Set(0f)));   // restore full health on return to the pool
    }
}
