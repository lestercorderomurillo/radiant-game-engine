using System;

namespace com.radiant.engine.bundle;

/// <summary>Controls the shared input-resolution preset for all UDR reconstruction algorithms.</summary>
/// <remarks>Performance, Balanced and Native use 25%, 50% and 100% of each output dimension.
/// These are resolution presets, not algorithms or HRC lighting-probe quality levels. Balanced is the default.</remarks>
public static class UDRQuality
{
    /// <summary>Per-axis resolution percentages: 25, 50 and 100.</summary>
    public static readonly int[] ScaleFactors = [25, 50, 100];
    /// <summary>Preset labels in index order: Performance, Balanced and Native.</summary>
    public static readonly string[] Names = ["Performance", "Balanced", "Native"];

    /// <summary>Gets the selected preset index; 1 (Balanced) by default.</summary>
    public static int Index { get; private set; } = 1;
    /// <summary>Gets the selected per-axis resolution as a percentage.</summary>
    public static int ScaleFactor => ScaleFactors[Index];
    /// <summary>Gets the selected per-axis resolution multiplier: 0.25, 0.5 or 1.</summary>
    public static float ScaleNormalized => ScaleFactor / 100f;

    /// <summary>Raised with the new index when the shared resolution preset changes.</summary>
    public static event Action<int> Changed;

    /// <summary>Selects a shared resolution preset and notifies active UDR systems.</summary>
    /// <param name="index">0 for Performance, 1 for Balanced, or 2 for Native. Values outside this range are clamped.</param>
    public static void Set(int index)
    {
        index = Math.Clamp(index, 0, ScaleFactors.Length - 1);

        if (Index != index)
        {
            Index = index;
            Changed?.Invoke(index);
        }
    }

    /// <summary>Advances to the next reconstruction quality preset.</summary>
    public static void Cycle()
    {
        Set((Index + 1) % ScaleFactors.Length);
    }
}
