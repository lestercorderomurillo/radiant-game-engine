using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

/// <summary>Diffuse SVGF adaptation: reprojected history, luminance moments, variance and first-level wavelet feedback.</summary>
public sealed class SvgfDenoiser : IDenoisingBackend
{
    public string Name => "SVGF (temporal diffuse)";
    public bool Available => true;
    public string Status => $"Temporal variance filtering: {HistoryFrames} frames";
    public int HistoryFrames { get; private set; }

    private RenderTarget2D History, Temporal, Moments, MomentWrite, Guides, GuideWrite, Output, Intermediate;
    private TraceCamera PreviousCamera;
    private long PreviousRevision = -1;
    private readonly Dictionary<string, Func<Texture2D>> Outputs;

    public IReadOnlyDictionary<string, Func<Texture2D>> Buffers => Outputs;

    /// <summary>Registers the temporal denoiser's diagnostic buffers.</summary>
    public SvgfDenoiser() { Outputs = new() { ["Temporal illumination"] = () => Temporal, ["Luminance moments"] = () => Moments, ["First-level history"] = () => History, ["Surface history"] = () => Guides, ["Wavelet intermediate"] = () => Intermediate }; }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public Texture2D GetOutput() => Output;

    /// <summary>Discards accumulated image history so the next frame starts fresh.</summary>
    public void ResetHistory() { HistoryFrames = 0; PreviousRevision = -1; }

    /// <summary>Allocates or resizes SVGF intermediates for the current input.</summary>
    private void Ensure(Renderer renderer, ref RenderTarget2D target, int width, int height, SurfaceFormat format = SurfaceFormat.Vector4)
    {
        if (target?.Width == width && target.Height == height && target.Format == format) return;

        renderer.SetTarget(null);
        target?.Dispose();
        target = renderer.CreateRenderTarget(width, height, format, usage: RenderTargetUsage.PreserveContents);
        ResetHistory();
    }

    /// <summary>Reprojects temporal radiance and runs guided spatial filtering.</summary>
    public void Render(in DenoisingFrame frame)
    {
        if (frame.SingleSample == null) throw new InvalidOperationException("SVGF requires unaccumulated SingleSample radiance. Request TraceChannels.Sample on the tracer.");

        var renderer = frame.Renderer;
        int width = frame.Input.Width, height = frame.Input.Height;

        if (frame.SingleSample.Width != width || frame.SingleSample.Height != height) throw new InvalidOperationException("SVGF sample and guide resolutions must match.");
        if (!frame.Camera.HasValue)
        {
            ResetHistory();
            Ensure(renderer, ref Output, width, height);
            renderer.Reset().SetTarget(Output).Clear(Color.Transparent).SetTarget(null);

            return;
        }

        Ensure(renderer, ref History, width, height);
        Ensure(renderer, ref Temporal, width, height);
        Ensure(renderer, ref Moments, width, height);
        Ensure(renderer, ref MomentWrite, width, height);
        Ensure(renderer, ref Guides, width, height, SurfaceFormat.HalfVector4);
        Ensure(renderer, ref GuideWrite, width, height, SurfaceFormat.HalfVector4);
        Ensure(renderer, ref Output, width, height);
        if (frame.Iterations > 2) Ensure(renderer, ref Intermediate, width, height);
        else if (Intermediate != null) { renderer.SetTarget(null); Intermediate.Dispose(); Intermediate = null; }

        var camera = frame.Camera.Value;
        if (PreviousRevision != frame.Revision || camera.Projection != PreviousCamera.Projection || camera.FarPlane != PreviousCamera.FarPlane || camera.Orthographic != PreviousCamera.Orthographic || Vector3.Distance(camera.Position, PreviousCamera.Position) > 2) ResetHistory();
        renderer.Reset().SetShader("Denoising/SVGF").SetTechnique("Temporal").Configure(SamplerState.PointClamp, 0).SetTargets(Temporal, MomentWrite, GuideWrite).SetParameter("Source", frame.SingleSample).SetParameter("Progressive", frame.Input).SetParameter("Albedo", frame.Albedo).SetParameter("Normals", frame.Normals).SetParameter("Depth", frame.Depth).SetParameter("Emission", frame.Emission ?? frame.Albedo).SetParameter("HasEmission", frame.Emission != null).SetParameter("History", History).SetParameter("Moments", Moments).SetParameter("Guides", Guides).SetParameter("HasHistory", HistoryFrames > 0).SetParameter("TexelSize", new Vector2(1f / width, 1f / height)).SetParameter("CurrentProjection", camera.Projection).SetParameter("InverseViewProjection", camera.InverseViewProjection).SetParameter("PreviousViewProjection", PreviousCamera.View * PreviousCamera.Projection).SetParameter("PreviousView", PreviousCamera.View).SetParameter("FarPlane", camera.FarPlane).SetParameter("PreviousFar", PreviousCamera.FarPlane).SetParameter("DepthSigma", frame.DepthSigma).SetParameter("NormalPower", frame.NormalPower).SetParameter("AlbedoSigma", frame.AlbedoSigma).Draw().Commit().SetTarget(null);
        (Moments, MomentWrite) = (MomentWrite, Moments);
        (Guides, GuideWrite) = (GuideWrite, Guides);
        // The first spatial level becomes next frame's illumination history, as in SVGF.
        Filter(frame, Temporal, History, 1, false);

        Texture2D current = History;

        for (int pass = 1; pass < frame.Iterations; pass++)
        {
            bool last = pass == frame.Iterations - 1;
            var target = last ? Output : (frame.Iterations - pass) % 2 == 1 ? Output : Intermediate;
            Filter(frame, current, target, 1 << pass, last);
            current = target;
        }

        if (frame.Iterations == 1)
        {
            renderer.Reset().SetShader("Denoising/SVGF").SetTechnique("Composite").Configure(SamplerState.PointClamp, 0).SetTarget(Output).SetParameter("History", History).SetParameter("Source", frame.SingleSample).SetParameter("Progressive", frame.Input).SetParameter("Albedo", frame.Albedo).SetParameter("Normals", frame.Normals).SetParameter("Emission", frame.Emission ?? frame.Albedo).SetParameter("HasEmission", frame.Emission != null).Draw().Commit().SetTarget(null);
        }

        PreviousCamera = camera;
        PreviousRevision = frame.Revision;
        HistoryFrames = Math.Min(HistoryFrames + 1, 4096);
    }

    /// <summary>Runs an edge-aware filtering pass over the denoising intermediates.</summary>
    private static void Filter(in DenoisingFrame frame, Texture2D source, RenderTarget2D target, int step, bool final)
    {
        frame.Renderer.Reset().SetShader("Denoising/SVGF").SetTechnique("Wavelet").Configure(SamplerState.PointClamp, 0).SetTarget(target).SetParameter("History", source).SetParameter("Source", frame.SingleSample).SetParameter("Progressive", frame.Input).SetParameter("Albedo", frame.Albedo).SetParameter("Normals", frame.Normals).SetParameter("Depth", frame.Depth).SetParameter("Emission", frame.Emission ?? frame.Albedo).SetParameter("HasEmission", frame.Emission != null).SetParameter("TexelSize", new Vector2(1f / frame.Input.Width, 1f / frame.Input.Height)).SetParameter("StepWidth", step).SetParameter("FinalPass", final).SetParameter("Strength", frame.Strength).SetParameter("DepthSigma", frame.DepthSigma).SetParameter("NormalPower", frame.NormalPower).SetParameter("AlbedoSigma", frame.AlbedoSigma).Draw().Commit().SetTarget(null);
    }

    /// <summary>Releases the temporal history and spatial filtering targets.</summary>
    public void Dispose()
    {
        History?.Dispose();
        Temporal?.Dispose();
        Moments?.Dispose();
        MomentWrite?.Dispose();
        Guides?.Dispose();
        GuideWrite?.Dispose();
        Output?.Dispose();
        Intermediate?.Dispose();
        History = Temporal = Moments = MomentWrite = Guides = GuideWrite = Output = Intermediate = null;
        ResetHistory();
    }
}
