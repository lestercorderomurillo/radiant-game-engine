using System;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>Per-view denoiser extension. Backends own their resources and report actual runtime support.</summary>
public interface IDenoisingBackend : IRenderSource, IDisposable
{
    string Name { get; }
    bool Available { get; }
    string Status { get; }
    void Render(in DenoisingFrame frame);
    void Update(in DenoisingFrame frame) { }
    void ResetHistory();
}

/// <summary>Linear HDR input and aligned guides. SingleSample is unaccumulated radiance, not the progressive mean.</summary>
public readonly record struct DenoisingFrame(Renderer Renderer, Texture2D Input, Texture2D SingleSample, Texture2D Albedo, Texture2D Normals, Texture2D Depth, Texture2D Emission, TraceCamera? Camera, long Revision, int Iterations, float Strength, float DepthSigma, float NormalPower, float AlbedoSigma);
