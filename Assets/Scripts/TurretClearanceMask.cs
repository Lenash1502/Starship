using UnityEngine;

// A baked table of which (yaw, pitch) orientations a turret is allowed to swing to.
//
// Two things are baked per cell, because they are different questions with different answers:
//   * Clear     -- the gun body fits there without burying itself in the hull.
//   * LineOfFire -- a shot fired from there does not pass through our own hull.
// A turret can legally point somewhere it must not shoot (bore clear of the wing, but the wingtip
// further along the line), so a single bit would be wrong.
//
// Blocked cells also store the nearest allowed cell, worked out once at bake time by a breadth-first
// flood from every allowed cell. Searching for a nearest-legal direction per frame instead produces
// visible jitter whenever the target sits on a boundary; a precomputed answer is stable.
public class TurretClearanceMask
{
    private readonly float yawMin;
    private readonly float pitchMin;
    private readonly float cellSize;
    private readonly int yawCells;
    private readonly int pitchCells;
    private readonly bool yawWraps;

    private readonly bool[] clear;
    private readonly bool[] lineOfFire;

    // For every cell, the index of the nearest cell that is clear. Allowed cells point at themselves.
    private readonly int[] nearestClear;

    public int CellCount => clear.Length;

    public TurretClearanceMask(float yawMin, float yawMax, float pitchMin, float pitchMax, float cellSize)
    {
        this.yawMin = yawMin;
        this.pitchMin = pitchMin;
        this.cellSize = Mathf.Max(cellSize, 0.5f);

        yawCells = Mathf.Max(Mathf.CeilToInt((yawMax - yawMin) / this.cellSize), 1);
        pitchCells = Mathf.Max(Mathf.CeilToInt((pitchMax - pitchMin) / this.cellSize), 1);

        // A turret with a full 360 traverse can slew the short way across the -180/+180 seam, so the
        // neighbour search has to know the grid joins up there.
        yawWraps = (yawMax - yawMin) >= 359.5f;

        clear = new bool[yawCells * pitchCells];
        lineOfFire = new bool[clear.Length];
        nearestClear = new int[clear.Length];
    }

    public int IndexOf(float yaw, float pitch)
    {
        int y = Mathf.Clamp(Mathf.RoundToInt((yaw - yawMin) / cellSize), 0, yawCells - 1);
        int p = Mathf.Clamp(Mathf.RoundToInt((pitch - pitchMin) / cellSize), 0, pitchCells - 1);
        return p * yawCells + y;
    }

    public void AnglesAt(int index, out float yaw, out float pitch)
    {
        yaw = yawMin + (index % yawCells) * cellSize;
        pitch = pitchMin + (index / yawCells) * cellSize;
    }

    public void SetCell(int index, bool isClear, bool hasLineOfFire)
    {
        clear[index] = isClear;
        lineOfFire[index] = hasLineOfFire;
    }

    public bool IsClear(int index) => clear[index];
    public bool HasLineOfFire(int index) => lineOfFire[index];

    // Snaps a desired orientation to the nearest one the gun can actually reach.
    public void ClampToClear(ref float yaw, ref float pitch)
    {
        int index = IndexOf(yaw, pitch);
        if (clear[index]) return;

        AnglesAt(nearestClear[index], out yaw, out pitch);
    }

    // Multi-source BFS out from every clear cell, so each blocked cell ends up pointing at the
    // closest clear one in grid steps. Run once, after the whole grid has been filled in.
    public void BuildNearestClear()
    {
        int[] queue = new int[clear.Length];
        int head = 0, tail = 0;

        for (int i = 0; i < clear.Length; i++)
        {
            if (clear[i])
            {
                nearestClear[i] = i;
                queue[tail++] = i;
            }
            else
            {
                nearestClear[i] = -1;
            }
        }

        // No orientation at all is clear -- leave everything pointing at itself and let the turret
        // hold its rest pose rather than snapping somewhere arbitrary.
        if (tail == 0)
        {
            for (int i = 0; i < nearestClear.Length; i++) nearestClear[i] = i;
            return;
        }

        while (head < tail)
        {
            int current = queue[head++];
            int y = current % yawCells;
            int p = current / yawCells;

            TryVisit(y - 1, p, current, queue, ref tail);
            TryVisit(y + 1, p, current, queue, ref tail);
            TryVisit(y, p - 1, current, queue, ref tail);
            TryVisit(y, p + 1, current, queue, ref tail);
        }
    }

    private void TryVisit(int y, int p, int from, int[] queue, ref int tail)
    {
        if (p < 0 || p >= pitchCells) return;

        if (y < 0 || y >= yawCells)
        {
            if (!yawWraps) return;
            y = (y + yawCells) % yawCells;
        }

        int index = p * yawCells + y;
        if (nearestClear[index] != -1) return;

        nearestClear[index] = nearestClear[from];
        queue[tail++] = index;
    }
}
