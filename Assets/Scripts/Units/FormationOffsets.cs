using UnityEngine;

/// <summary>
/// Generates near-square grid formation offsets: column count derived from
/// the square root of unit count, rows stacked front-to-back (row 0 = front),
/// every row centered, partial last row centered too. The
/// caller rotates this local pattern (local +Z = front) to face wherever
/// the group is actually heading - see MovementOrderIssuer.
/// </summary>
public static class FormationOffsets
{
        private const float RowSpacing = 1.5f;   // front-to-back distance between rows
    private const float UnitSpacing = 1.2f;  // left-right distance between units in the same row

public static Vector3[] GetOffsets(int unitCount)
    {
        Vector3[] offsets = new Vector3[unitCount];
        if (unitCount == 0)
            return offsets;

        // As close to a square as possible: columns from the square root,
        // rows as needed. 10 -> 4+4+2, 9 -> 3+3+3, 5 -> 3+2.
        int columns = Mathf.CeilToInt(Mathf.Sqrt(unitCount));
        int placed = 0;
        int row = 0;

        while (placed < unitCount)
        {
            int remaining = unitCount - placed;
            int countInRow = Mathf.Min(columns, remaining);

            // Center each row on local X = 0 - this also makes a partial
            // last row sit centered instead of hanging off one side.
            float rowWidth = (countInRow - 1) * UnitSpacing;
            float startX = -rowWidth / 2f;

            // Row 0 is the front (leading edge, toward the destination);
            // each row behind it sits further back along local -Z.
            float z = -row * RowSpacing;

            for (int i = 0; i < countInRow; i++)
            {
                offsets[placed] = new Vector3(startX + i * UnitSpacing, 0f, z);
                placed++;
            }

            row++;
        }

        return offsets;
    }
}
