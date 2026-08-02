using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>Basic health system used by buildings, resource sites and enemies.</summary>
public partial class Health : SpokeNode {

    [Export] public Unit Unit { get; set; }

    [Export] float maxHp { get => _maxHp.Now; set => _maxHp.Set(value); }
    [Export] float damage { get => _damage.Now; set => _damage.Set(value); }

    readonly State<float> _maxHp = State.Create(1f);
    readonly State<float> _damage = State.Create(0f);
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
        _damage.Update(x => x + amount);
        damaged.Invoke();
    }

    public void Repair(float amount) {
        if (!isAlive.Now) return;
        _damage.Update(x => Mathf.Max(0f, x - amount));
    }

    protected override void Init(EffectBuilder s) {
        var hp = s.Memo(s => s.D(_maxHp) - s.D(_damage));

        s.Effect(s => {
            hpFrac.Set(s.D(hp) / s.D(_maxHp));
            isAlive.Set(s.D(hp) > 0f);
        });

        s.Phase(IsInTree, s => s.OnCleanup(() => _damage.Set(0f)));   // restore full health on return to the pool
    }
}
