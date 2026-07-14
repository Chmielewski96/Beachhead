using System;
using UnityEngine;

/// <summary>
/// Generic health for ANYTHING damageable: Keep, walls, units, crabs.
/// This deliberately does LESS than Turtling's Enemy.cs, which bundled
/// health + death behavior + popups + health bar into one class. Here,
/// Health only tracks numbers and raises events - whoever cares about a
/// death (Keep triggers game over, a wall vanishes, a crab topples)
/// subscribes its own handler. Health never knows who's listening.
/// </summary>
public class Health : MonoBehaviour
{
    [SerializeField] private int maxHP = 100;

    public int Max => maxHP;
    public int Current { get; private set; }
    public bool IsDead { get; private set; }

    /// <summary>Fired on every HP change: (current, max).</summary>
    public event Action<int, int> OnDamaged;

    /// <summary>Fired exactly once, when HP first reaches zero.</summary>
    public event Action OnDeath;

    private void Awake()
    {
        Current = maxHP;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead)
            return;

        Current = Mathf.Max(0, Current - amount);
        OnDamaged?.Invoke(Current, maxHP);

        if (Current == 0)
        {
            IsDead = true;
            OnDeath?.Invoke();
        }
    }

/// <summary>
    /// Restores HP, clamped to Max. Fires OnDamaged (it's really an
    /// on-HP-changed event) so health bars refresh - for buildings that
    /// also pops the fade-on-hit bar visible briefly, which doubles as
    /// free repair feedback. Dead things stay dead - no resurrection.
    /// </summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0)
            return;

        Current = Mathf.Min(maxHP, Current + amount);
        OnDamaged?.Invoke(Current, maxHP);
    }


/// <summary>Damage committed by in-flight projectiles (see Projectile) - lets towers avoid overkill.</summary>
    public int PendingDamage { get; private set; }

    /// <summary>HP left once all in-flight damage lands. At or below zero, shooting this target again is a wasted shot.</summary>
    public int EffectiveHP => Current - PendingDamage;

    public void ReservePendingDamage(int amount)
    {
        if (amount > 0)
            PendingDamage += amount;
    }

    public void ReleasePendingDamage(int amount)
    {
        if (amount > 0)
            PendingDamage = Mathf.Max(0, PendingDamage - amount);
    }

}
