using Godot;

namespace Spoke.Examples.BaseDefence;

/// <summary>
/// The health of a unit. Not a node — it has no position and nothing to draw, so making it one
/// would only add a line to every unit's scene tree.
///
/// Unity models this as a separate MonoBehaviour because that's how Unity composes behaviour onto
/// an object. Godot composes by nesting nodes, which is the wrong shape for a bag of reactive
/// numbers; a plain class the unit owns and mounts is closer to the intent, and it costs the unit
/// one line: s.Effect(health.Mount).
/// </summary>
public class Health {

    readonly State<float> maxHp = State.Create(1f);
    readonly State<float> damage = State.Create(0f);
    readonly State<float> hpFraction = State.Create(1f);
    readonly State<bool> isAlive = State.Create(true);
    readonly Trigger damaged = Trigger.Create();

    /// <summary>Full health, in hit points. Driven by the owning unit's exported MaxHp.</summary>
    public float MaxHp {
        get => maxHp.Now;
        set => maxHp.Set(value);
    }

    /// <summary>Current HP as a fraction of max, from 1 down to 0.</summary>
    public ISignal<float> HPFraction => hpFraction;

    /// <summary>True while HP is above zero.</summary>
    public ISignal<bool> IsAlive => isAlive;

    /// <summary>Fires each time the unit takes damage.</summary>
    public ITrigger Damaged => damaged;

    public void Damage(float amount) {
        if (!isAlive.Now) return;
        damage.Update(x => x + amount);
        damaged.Invoke();
    }

    public void Repair(float amount) {
        if (!isAlive.Now) return;
        damage.Update(x => Mathf.Max(0f, x - amount));
    }

    /// <summary>Mounted by the owning unit. Derives the fraction and the alive flag from damage taken.</summary>
    public EffectBlock Mount => s => {
        var hp = s.Memo(s => s.D(maxHp) - s.D(damage));

        s.Effect(s => {
            hpFraction.Set(s.D(hp) / s.D(maxHp));
            isAlive.Set(s.D(hp) > 0f);
        });

        // Full health on the way back to the pool. This is the only reset the whole game writes by
        // hand, because damage is the only unit state that isn't itself scoped to a phase.
        s.OnCleanup(() => damage.Set(0f));
    };
}
