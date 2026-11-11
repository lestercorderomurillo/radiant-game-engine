using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Resizes the supplied image using bilinear filtering.</summary>
public class BilinearUpscaler : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;

    private Func<Texture2D> InputSource;

    /// <inheritdoc/>
    public override void Initialize()
    {
        ApplyRenderScale();

        Inspector.CreateWindow("bilinear", "Bilinear", Visible: false);
        Inspector.AddLabel("bilinear", "quality", "...");
        Inspector.AddLabel("bilinear", "input", "...");
        Inspector.AddLabel("bilinear", "output", "...");
        Inspector.AddDropdown("bilinear", "qualityDrop", "Quality", UDRQuality.Names, UDRQuality.Index, UDRQuality.Set);
        UDRQuality.Changed += OnQualityChanged;
    }

    /// <summary>Sets the live image provider consumed by this stage.</summary>
    public void SetInputSource(Func<Texture2D> source)
    {
        InputSource = source;
    }

    /// <summary>Applies the selected reconstruction quality and refreshes dependent resources.</summary>
    private void OnQualityChanged(int index)
    {
        ApplyRenderScale();
        Inspector.SetDropdownValue("bilinear", "qualityDrop", index);
    }

    /// <summary>Updates the renderer scale to match the selected reconstruction quality.</summary>
    private void ApplyRenderScale()
    {
        Renderer.RenderScale = UDRQuality.ScaleNormalized;
    }

    /// <inheritdoc/>
    public override void Update()
    {
        if (InputSource == null) return;

        var input = InputSource();

        if (input == null) return;

        Renderer.SetTarget(null);

        Inspector.SetLabel("bilinear", "quality", $"Quality: {UDRQuality.Names[UDRQuality.Index]} ({UDRQuality.ScaleNormalized:P0})");
        Inspector.SetLabel("bilinear", "input", $"Input Size: {input.Width}x{input.Height}");
        Inspector.SetLabel("bilinear", "output", $"Output Size: {Renderer.ScreenSize.X}x{Renderer.ScreenSize.Y}");
    }

    /// <inheritdoc/>
    public override void Render()
    {
        if (InputSource == null || UDRQuality.ScaleFactor == 100) return;

        var input = InputSource();

        if (input == null) return;

        Renderer.Blit(input, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        UDRQuality.Changed -= OnQualityChanged;
        Inspector.DestroyWindow("bilinear");
        Renderer.RenderScale = 1.0f;
    }
}
