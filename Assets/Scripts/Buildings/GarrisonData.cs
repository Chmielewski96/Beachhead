using UnityEngine;

/// <summary>
/// Garrison building stats - the second ScriptableObject inheriting from
/// BuildingData, so placement (ghost, snap, validity, costs) works with
/// zero new code. Note: garrison assets should set costResource = Shells,
/// making this the first building paid for with combat income.
/// </summary>
[CreateAssetMenu(menuName = "Beachhead/Garrison Data")]
public class GarrisonData : BuildingData
{
    [Header("Garrison")]
    public GameObject soldierPrefab;

    [Tooltip("Troop pool size the building maintains before any Reinforce upgrades.")]
    public int maxGarrison = 3;

    [Tooltip("Seconds between replacement spawns while below the cap.")]
    public float respawnCooldown = 8f;

    [Tooltip("How far troops wander from the building while patrolling.")]
    public float patrolRadius = 8f;

    [Header("Reinforce Upgrade")]
    [Tooltip("Shell cost per +1 max garrison.")]
    public int reinforceCost = 15;

    [Tooltip("How many times the garrison can be reinforced.")]
    public int maxReinforcements = 5;
}
