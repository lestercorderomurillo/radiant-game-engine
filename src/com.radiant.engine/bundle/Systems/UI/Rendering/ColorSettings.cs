using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional color controls for any view. Bind by instance; assign distinct IDs for multiple views.</summary>
[SystemTag("Rendering", "Tools")]
public class ColorSettings : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;
    public ToneMapping Source;
    public string Id = "colorProfile";
    public string Title = "Color Management";
    private static readonly string[] MapperNames = ["None", "Reinhard", "ACES", "ACES Fitted", "AgX", "Filmic", "Uchimura", "PBR Neutral"];
    private static readonly string[] TransferNames = ["Linear", "sRGB", "Gamma 2.2"];
    public override void Initialize()
    {
        if (Source == null) throw new InvalidOperationException("ColorSettings requires a ToneMapping source.");
        CreateWindow(Id, Title, Source);
    }
    internal static void CreateWindow(string id, string title, ToneMapping source)
    {
        if (id == null) return;
        Inspector.CreateWindow(id, title, Visible: false);
        Inspector.AddToggle(id, "enabled", "Enabled", source.Enabled, enabled => source.Enabled = enabled);
        Inspector.AddDropdown(id, "tonemap", "Tonemapper", MapperNames, (int)source.Mapper, index => source.Mapper = (ToneMapper)index);
        Inspector.AddSlider(id, "exposure", "Exposure multiplier", 0.1f, 8, source.Exposure, value => source.Exposure = value);
        Inspector.AddDropdown(id, "transfer", "Output transfer", TransferNames, (int)source.Transfer, index => source.Transfer = (ColorTransfer)index);
    }
    internal static void SyncWindow(string id, ToneMapping source)
    {
        if (id == null) return;
        Inspector.SetToggleValue(id, "enabled", source.Enabled);
        Inspector.SetDropdownValue(id, "tonemap", (int)source.Mapper);
        Inspector.SetSliderValue(id, "exposure", source.Exposure);
        Inspector.SetDropdownValue(id, "transfer", (int)source.Transfer);
        Inspector.SetWidgetEnabled(id, "tonemap", source.Enabled);
        Inspector.SetWidgetEnabled(id, "exposure", source.Enabled);
        Inspector.SetWidgetEnabled(id, "transfer", source.Enabled);
    }
    public override void Update() => SyncWindow(Id, Source);
    public override void Dispose() => Inspector.DestroyWindow(Id);
}
