using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional per-view AA controls shared by 2D and 3D pipelines.</summary>
[SystemTag("Rendering", "Tools")]
public class AntiAliasingSettings : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;
    public AntiAliasing Source;
    public RasterSampling Sampling;
    public string Id = "antialiasing";
    public string Title = "Antialiasing";
    public override void Initialize()
    {
        if (Source == null) throw new InvalidOperationException("AntiAliasingSettings requires an AntiAliasing source.");
        Inspector.CreateWindow(Id, Title, Visible: false);
        if (Sampling != null)
        {
            Inspector.AddToggle(Id, "geometryEnabled", "Geometry AA enabled", Sampling.Enabled, enabled => { Sampling.Enabled = enabled; Source.ResetHistory(); });
            Inspector.AddDropdown(Id, "geometryAA", "Geometry AA", Enum.GetNames<RasterAAMode>(), (int)Sampling.Mode, index => { Sampling.Mode = (RasterAAMode)index; Source.ResetHistory(); });
            Inspector.AddLabel(Id, "status", Sampling.Status);
        }
        Inspector.AddToggle(Id, "enabled", "Post AA enabled", Source.Enabled, enabled => { Source.Enabled = enabled; Source.ResetHistory(); });
        Inspector.AddDropdown(Id, "postAA", "Post AA", Enum.GetNames<PostAAMode>(), (int)Source.Mode, index => { Source.Mode = (PostAAMode)index; Source.ResetHistory(); });
        Inspector.AddSlider(Id, "threshold", "FXAA edge threshold", 0.0312f, 0.25f, Source.EdgeThreshold, value => Source.EdgeThreshold = value);
        Inspector.AddSlider(Id, "history", "TAA history weight", 0, 0.99f, Source.HistoryWeight, value => Source.HistoryWeight = value);
        Inspector.AddButton(Id, "reset", "Reset temporal history", Source.ResetHistory);
    }
    public override void Update()
    {
        if (Sampling != null)
        {
            Inspector.SetToggleValue(Id, "geometryEnabled", Sampling.Enabled);
            Inspector.SetDropdownValue(Id, "geometryAA", (int)Sampling.Mode);
            Inspector.SetWidgetEnabled(Id, "geometryAA", Sampling.Enabled);
            Inspector.SetLabel(Id, "status", Sampling.Status);
        }
        Inspector.SetToggleValue(Id, "enabled", Source.Enabled);
        Inspector.SetDropdownValue(Id, "postAA", (int)Source.Mode);
        Inspector.SetSliderValue(Id, "threshold", Source.EdgeThreshold);
        Inspector.SetSliderValue(Id, "history", Source.HistoryWeight);
        Inspector.SetWidgetEnabled(Id, "postAA", Source.Enabled);
        Inspector.SetWidgetEnabled(Id, "threshold", Source.Enabled && Source.Mode == PostAAMode.FXAA);
        Inspector.SetWidgetEnabled(Id, "history", Source.Enabled && Source.Mode == PostAAMode.TAA);
    }
    public override void Dispose() => Inspector.DestroyWindow(Id);
}
