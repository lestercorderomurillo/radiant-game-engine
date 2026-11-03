using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

/// <summary>HRC/UDR update-phase adapter for the shared color processor. New render-phase pipelines use ToneMapping.</summary>
[RunAfter(typeof(HRCGI))]
[RunBefore(typeof(Bilinear), typeof(UDR1), typeof(UDR2), typeof(UDR3), typeof(UDR4))]
public class ColorManagement : ToneMapping
{
    public string SettingsId = "colorProfile";
    public ColorManagement() { Mapper = ToneMapper.PBRNeutral; Transfer = ColorTransfer.Gamma22; }
    public void SetInputSource(Func<Texture2D> source) => Input = source;
    public override void Initialize() => ColorSettings.CreateWindow(SettingsId, "Color Management", this);
    public override void Update() { ColorSettings.SyncWindow(SettingsId, this); base.Render(); }
    public override void Render()
    {
        var output = GetOutput();
        if (output != null) Renderer.Blit(output, BlendState.AlphaBlend, SamplerState.PointClamp);
    }
    public override void Dispose() { base.Dispose(); if (SettingsId != null) Inspector.DestroyWindow(SettingsId); }
}
