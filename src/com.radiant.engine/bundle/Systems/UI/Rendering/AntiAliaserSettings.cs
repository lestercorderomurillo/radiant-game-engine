using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional per-view AA controls shared by 2D and 3D pipelines.</summary>
[SystemTag("Rendering", "Tools")]
public class AntiAliaserSettings : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;
    public AntiAliaser Source;
    public RasterSampler Sampling;
    public Upscaler Upscaler;

    private UpscalerMode[] UpscalingModes;
    private PostAAMode[] PostModes;

    public string Id = "antialiasing";
    public string Title = "Antialiasing";

    /// <inheritdoc/>
    public override void Initialize()
    {
        if (Source == null) throw new InvalidOperationException("AntiAliaserSettings requires an AntiAliaser source.");

        Inspector.CreateWindow(Id, Title, Visible: false);

        if (Upscaler != null)
        {
            UpscalingModes = Enum.GetValues<UpscalerMode>();
            Inspector.AddDropdown(Id, "upscaler", "Reconstruction", Array.ConvertAll(UpscalingModes, mode => mode.ToString()), Array.IndexOf(UpscalingModes, Upscaler.Mode), index => { Upscaler.Select(UpscalingModes[index]); Source.ResetHistory(); });
            Inspector.AddDropdown(Id, "quality", "Reconstruction quality", UDRQuality.Names, UDRQuality.Index, index => { UDRQuality.Set(index); Source.ResetHistory(); });
            Inspector.AddLabel(Id, "reconstructionStatus", Upscaler.Status);
        }

        if (Sampling != null)
        {
            Inspector.AddToggle(Id, "geometryEnabled", "Geometry AA enabled", Sampling.Enabled, enabled => { Sampling.Enabled = enabled; Source.ResetHistory(); });
            Inspector.AddDropdown(Id, "geometryAA", "Geometry AA", Enum.GetNames<RasterAAMode>(), (int)Sampling.Mode, index => { Sampling.Mode = (RasterAAMode)index; Source.ResetHistory(); });
            Inspector.AddLabel(Id, "status", Sampling.Status);
        }

        Inspector.AddToggle(Id, "enabled", "Post AA enabled", Source.Enabled, enabled => { Source.Enabled = enabled; Source.ResetHistory(); });
        PostModes = Source.Camera != null ? Enum.GetValues<PostAAMode>() : [PostAAMode.None, PostAAMode.FXAA, PostAAMode.SMAA];
        Inspector.AddDropdown(Id, "postAA", "Post AA", Array.ConvertAll(PostModes, mode => mode.ToString()), Array.IndexOf(PostModes, Source.Mode), index => { Source.Mode = PostModes[index]; Source.ResetHistory(); });
        Inspector.AddSlider(Id, "threshold", "FXAA edge threshold", 0.0312f, 0.25f, Source.EdgeThreshold, value => Source.EdgeThreshold = value);
        Inspector.AddSlider(Id, "history", "TAA history weight", 0, 0.99f, Source.HistoryWeight, value => Source.HistoryWeight = value);
        Inspector.AddButton(Id, "reset", "Reset temporal history", () => { Source.ResetHistory(); Upscaler?.ResetHistory(); });
    }

    /// <inheritdoc/>
    public override void Update()
    {
        if (Upscaler != null)
        {
            Inspector.SetDropdownValue(Id, "upscaler", Array.IndexOf(UpscalingModes, Upscaler.Mode));
            Inspector.SetDropdownValue(Id, "quality", UDRQuality.Index);
            Inspector.SetWidgetEnabled(Id, "quality", Upscaler.Mode != UpscalerMode.None);
            Inspector.SetLabel(Id, "reconstructionStatus", Upscaler.Status);
        }

        if (Sampling != null)
        {
            Inspector.SetToggleValue(Id, "geometryEnabled", Sampling.Enabled);
            Inspector.SetDropdownValue(Id, "geometryAA", (int)Sampling.Mode);
            Inspector.SetWidgetEnabled(Id, "geometryAA", Sampling.Enabled);
            Inspector.SetLabel(Id, "status", Sampling.Status);
        }

        Inspector.SetToggleValue(Id, "enabled", Source.Enabled);
        Inspector.SetDropdownValue(Id, "postAA", Array.IndexOf(PostModes, Source.Mode));
        Inspector.SetSliderValue(Id, "threshold", Source.EdgeThreshold);
        Inspector.SetSliderValue(Id, "history", Source.HistoryWeight);
        Inspector.SetWidgetEnabled(Id, "postAA", Source.Enabled);
        Inspector.SetWidgetEnabled(Id, "threshold", Source.Enabled && Source.Mode == PostAAMode.FXAA);
        Inspector.SetWidgetEnabled(Id, "history", Source.Enabled && Source.Mode == PostAAMode.TAA);
    }

    /// <inheritdoc/>
    public override void Dispose() => Inspector.DestroyWindow(Id);
}
