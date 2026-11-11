using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>HRC/UDR update-phase adapter for the shared color processor. New render-phase pipelines use ToneMapper.</summary>
[RunAfter(typeof(HRCGI))]
[RunBefore(typeof(BilinearUpscaler), typeof(UDR1), typeof(UDR2), typeof(UDR3), typeof(UDR4))]
public class HRCToneMapper : ToneMapper
{
    public string SettingsId = "colorProfile";

    /// <summary>Selects neutral tone mapping with gamma-encoded output for the HRC adapter.</summary>
    public HRCToneMapper() { Operator = ToneMapOperator.PBRNeutral; Transfer = ColorTransfer.Gamma22; }

    /// <summary>Sets the live image provider consumed by this stage.</summary>
    public void SetInputSource(Func<Texture2D> source) => Input = source;

    /// <inheritdoc/>
    public override void Initialize() => ToneMapperSettings.CreateWindow(SettingsId, "Color Management", this);

    /// <inheritdoc/>
    public override void Update() { ToneMapperSettings.SyncWindow(SettingsId, this); base.Render(); }

    /// <inheritdoc/>
    public override void Render()
    {
        var output = GetOutput();
        if (output != null) Renderer.Blit(output, BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <inheritdoc/>
    public override void Dispose() { base.Dispose(); if (SettingsId != null) Inspector.DestroyWindow(SettingsId); }
}
