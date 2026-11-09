using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>
/// Add this component to entities that need motion vector tracking.
/// Contains a fixed-size circular buffer for position history - no allocations.
/// </summary>
[ComponentDescription("Tracks position history for motion vector generation.")]
public struct MotionTrackable : Component
{
    private const int MaxHistory = 4;

    private Vector2 Pos0, Pos1, Pos2, Pos3;
    private int Head;
    private int HistoryCount;

    /// <summary>Initializes an empty position-history ring for motion reconstruction.</summary>
    public MotionTrackable()
    {
        Pos0 = Pos1 = Pos2 = Pos3 = Vector2.Zero;
        Head = 0;
        HistoryCount = 0;
    }

    /// <summary>Records a position in the motion-history ring buffer.</summary>
    public void Push(Vector2 position)
    {
        switch (Head)
        {
            case 0: Pos0 = position;
            break;
            case 1: Pos1 = position;
            break;
            case 2: Pos2 = position;
            break;
            case 3: Pos3 = position;
            break;
        }

        Head = (Head + 1) % MaxHistory;
        if (HistoryCount < MaxHistory) HistoryCount++;
    }

    /// <summary>Estimates velocity from the retained motion samples.</summary>
    public readonly Vector2 CalculateVelocity(Vector2 currentPos, int historyFrames)
    {
        if (HistoryCount == 0) return Vector2.Zero;

        int framesToUse = Math.Min(HistoryCount, Math.Min(historyFrames, MaxHistory));

        if (framesToUse == 0) return Vector2.Zero;

        Vector2 weightedVelocity = Vector2.Zero;
        float totalWeight = 0;

        int start = (Head - HistoryCount + MaxHistory) % MaxHistory;
        Vector2 prev = GetAt(start);

        for (int i = 1; i < framesToUse; i++)
        {
            int idx = (start + i) % MaxHistory;
            Vector2 pos = GetAt(idx);

            float weight = i;
            weightedVelocity.X += (pos.X - prev.X) * weight;
            weightedVelocity.Y += (pos.Y - prev.Y) * weight;
            totalWeight += weight;
            prev = pos;
        }

        float finalWeight = framesToUse;
        weightedVelocity.X += (currentPos.X - prev.X) * finalWeight;
        weightedVelocity.Y += (currentPos.Y - prev.Y) * finalWeight;
        totalWeight += finalWeight;

        return totalWeight > 0 ? weightedVelocity / totalWeight : Vector2.Zero;
    }

    /// <summary>Displacement between consecutive rounded positions actually used by Geometry.</summary>
    public readonly Vector2 RenderedDisplacement(Vector2 currentPosition)
    {
        if (HistoryCount == 0) return Vector2.Zero;

        Vector2 previous = GetAt((Head - 1 + MaxHistory) % MaxHistory);
        return new Vector2(MathF.Round(currentPosition.X) - MathF.Round(previous.X),
            MathF.Round(currentPosition.Y) - MathF.Round(previous.Y));
    }

    /// <summary>Reads a position sample relative to the motion-history ring.</summary>
    private readonly Vector2 GetAt(int idx)
    {
        return idx switch
        {
            0 => Pos0,
            1 => Pos1,
            2 => Pos2,
            3 => Pos3,
            _ => Vector2.Zero
        };
    }
}
