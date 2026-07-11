using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The stockpile for ALL resource types, keyed by ResourceType. Everything
/// downstream is event-driven: counters listen to OnResourceChanged (never
/// poll) and failure feedback listens to OnSpendFailed - both carry the
/// resource type so each listener filters for the one it cares about.
/// </summary>
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("Starting Stockpile")]
    [SerializeField] private int startingSand = 100;
    [SerializeField] private int startingShells = 20;

    private readonly Dictionary<ResourceType, int> stockpile = new Dictionary<ResourceType, int>();

    /// <summary>Fired on every balance change: (which resource, new total).</summary>
    public event Action<ResourceType, int> OnResourceChanged;

    /// <summary>Fired when a TrySpend is refused - drive 'can't afford' feedback off this.</summary>
    public event Action<ResourceType> OnSpendFailed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        stockpile[ResourceType.Sand] = startingSand;
        stockpile[ResourceType.Shells] = startingShells;
    }

    private void Start()
    {
        // Announce every starting balance once, so UI that subscribed in its
        // own Start initializes without polling. (Keys copied to a list so a
        // listener reacting by spending can't invalidate the iteration.)
        foreach (ResourceType type in new List<ResourceType>(stockpile.Keys))
            OnResourceChanged?.Invoke(type, stockpile[type]);
    }

    public int GetAmount(ResourceType type)
    {
        return stockpile.TryGetValue(type, out int amount) ? amount : 0;
    }

    public bool TrySpend(ResourceType type, int amount)
    {
        if (amount < 0 || GetAmount(type) < amount)
        {
            OnSpendFailed?.Invoke(type);
            return false;
        }

        stockpile[type] = GetAmount(type) - amount;
        OnResourceChanged?.Invoke(type, stockpile[type]);
        return true;
    }

    public void Add(ResourceType type, int amount)
    {
        if (amount <= 0)
            return;

        stockpile[type] = GetAmount(type) + amount;
        OnResourceChanged?.Invoke(type, stockpile[type]);
    }
}
