using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional per-view controls for a <see cref="RCGI"/> stage: probe density, directions, ray length and bounces.</summary>
[SystemTag("Rendering", "Tools")]
public class RCGISettings : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;
    public RCGI Source;
    public string Id = "radianceCascades";
    public string Title = "Radiance cascades";
    public Action Changed;

    private static readonly int[] GridSizes = [8, 16, 32, 64];
    private static readonly int[] DirectionRows = [2, 4, 8];

    /// <inheritdoc/>
    public override void Initialize()
    {
        if (Source == null) throw new InvalidOperationException("RCGISettings requires a RCGI source.");

        Inspector.CreateWindow(Id, Title, LayoutOrder: 200, Visible: false);
        Inspector.AddLabel(Id, "status", "Radiance cascades inactive");
        Inspector.AddDropdown(Id, "grid", "Probe grid", Array.ConvertAll(GridSizes, size => $"{size}³"), Array.IndexOf(GridSizes, Source.GridSize), index => { Source.GridSize = GridSizes[index]; if (Source.GridSize == 64) Source.BaseDirectionRows = Math.Min(Source.BaseDirectionRows, 4); Changed?.Invoke(); });
        Inspector.AddDropdown(Id, "directions", "Cascade 0 directions", Array.ConvertAll(DirectionRows, rows => (2 * rows * rows).ToString()), Array.IndexOf(DirectionRows, Source.BaseDirectionRows), index => { Source.BaseDirectionRows = DirectionRows[index]; if (Source.BaseDirectionRows == 8) Source.GridSize = Math.Min(Source.GridSize, 32); Changed?.Invoke(); });
        Inspector.AddSlider(Id, "interval", "Cascade 0 ray length (spacings)", 0.5f, 4, Source.IntervalScale, value => { Source.IntervalScale = value; Changed?.Invoke(); });
        Inspector.AddSlider(Id, "bias", "Normal bias (spacings)", 0, 1, Source.NormalBias, value => { Source.NormalBias = value; Changed?.Invoke(); });
        Inspector.AddToggle(Id, "bounces", "Multiple bounces", Source.MultiBounce, enabled => { Source.MultiBounce = enabled; Changed?.Invoke(); });
        Inspector.AddSlider(Id, "bounceLimit", "Bounce updates (0 = continuous)", 0, 128, Source.MaxBounceUpdates, value => { Source.MaxBounceUpdates = (int)value; Changed?.Invoke(); });
        Inspector.AddToggle(Id, "leakFix", "Visibility-aware merging (8 rays)", Source.LeakFix, enabled => { Source.LeakFix = enabled; Changed?.Invoke(); });
        Inspector.AddButton(Id, "reset", "Reset bounce feedback", () => { Source.ResetBounces(); Changed?.Invoke(); });
        Inspector.AddLabel(Id, "volume", "");
    }

    /// <inheritdoc/>
    public override void Update()
    {
        Inspector.SetLabel(Id, "status", Source.Enabled ? $"{Source.CascadeCount} cascades | {(Source.ReusedProbeField ? "cached" : $"{Source.RaysPerFrame / 1000} k interval rays/update")} | bounce updates: {Source.BounceFrames}" : "Radiance cascades inactive");
        Inspector.SetDropdownValue(Id, "grid", Math.Max(0, Array.IndexOf(GridSizes, Source.GridSize)));
        Inspector.SetDropdownValue(Id, "directions", Math.Max(0, Array.IndexOf(DirectionRows, Source.BaseDirectionRows)));
        Inspector.SetSliderValue(Id, "interval", Source.IntervalScale);
        Inspector.SetSliderValue(Id, "bias", Source.NormalBias);
        Inspector.SetToggleValue(Id, "bounces", Source.MultiBounce);
        Inspector.SetSliderValue(Id, "bounceLimit", Source.MaxBounceUpdates);
        Inspector.SetToggleValue(Id, "leakFix", Source.LeakFix);
        Inspector.SetLabel(Id, "volume", Source.Enabled ? $"Probe spacing {Source.ProbeSpacing:F3} | volume {Source.VolumeSize:F2} at {Source.VolumeMin + new Vector3(Source.VolumeSize * 0.5f)}" : "");
    }

    /// <inheritdoc/>
    public override void Dispose() => Inspector.DestroyWindow(Id);
}
