/// <summary>
/// All resource kinds in the economy. Beachhead uses two: Sand (building
/// material, gathered by Workers from deposits) and Shells (unit/upgrade
/// currency, dropped only by killed enemies - mirroring the north star
/// design where shells come from combat and exploration, never mining).
/// Adding a resource later (sticks, stones...) = add an enum entry, a
/// source for it, and costs that use it. Zero systems code.
/// </summary>
public enum ResourceType
{
    Sand,
    Shells,
}
