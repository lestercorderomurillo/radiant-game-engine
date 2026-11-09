using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

/// <summary>Optional geometry buffers to generate alongside the primary color output.</summary>
[Flags]
public enum GeometryChannels { Color = 1, Normals = 2, Depth = 4, All = Color | Normals | Depth }

/// <summary>Unlit raster geometry buffers. Does not select a preview or present to the screen.</summary>
[RunAfter(typeof(MeshGeometry3D))]
[SystemTag("3D", "Geometry")]
public class Geometry3D : core.System, IRenderSource, IRasterGeometry
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    public MeshGeometry3D Source;
    public GeometryChannels Channels = GeometryChannels.Color;
    public DepthFormat VisibilityFormat = DepthFormat.Depth16;

    DepthFormat IRasterGeometry.VisibilityFormat => VisibilityFormat;

    public Point Size => Source.Size;
    public int RequestedSamples => Source.Sampling?.RequestedSamples ?? 0;

    private DepthFormat AllocatedVisibility;
    private GeometryChannels AllocatedChannels;
    private int AllocatedSamples;

    public RenderTarget2D ColorTexture { get; private set; }
    public RenderTarget2D NormalTexture { get; private set; }
    public RenderTarget2D DepthTexture { get; private set; }
    public int MeshCount { get; private set; }

    private readonly Dictionary<string, Func<Texture2D>> Outputs;

    public IReadOnlyDictionary<string, Func<Texture2D>> Buffers => Outputs;

    /// <summary>Registers the 3D geometry buffers exposed to inspection.</summary>
    public Geometry3D() { Outputs = new() { ["Color"] = () => ColorTexture, ["Normals"] = () => NormalTexture, ["Depth"] = () => DepthTexture }; }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public Texture2D GetOutput() => ColorTexture;

    /// <summary>Builds a mesh world transform from position, rotation, and scale.</summary>
    public static Matrix WorldMatrix(in Transform3D transform) => transform.Matrix;

    /// <inheritdoc/>
    public override void Initialize()
    {
        if (Source == null) throw new InvalidOperationException("Geometry3D requires a MeshGeometry3D source.");

        CreateTargets();
    }

    /// <summary>Allocates the enabled geometry buffers at the current sampling size.</summary>
    private void CreateTargets()
    {
        int requested = Source.Sampling?.RequestedSamples ?? 0;
        int samples = Renderer.GetSupportedSamples(requested, SurfaceFormat.Color, VisibilityFormat);
        if ((Channels & GeometryChannels.Depth) != 0) samples = Math.Min(samples, Renderer.GetSupportedSamples(requested, SurfaceFormat.Single, VisibilityFormat));
        ColorTexture = Renderer.CreateRenderTarget(Source.Size.X, Source.Size.Y, depth: VisibilityFormat, samples: samples);
        if ((Channels & GeometryChannels.Normals) != 0) NormalTexture = Renderer.CreateRenderTarget(Source.Size.X, Source.Size.Y, depth: samples > 0 ? VisibilityFormat : DepthFormat.None, samples: samples);
        if ((Channels & GeometryChannels.Depth) != 0) DepthTexture = Renderer.CreateRenderTarget(Source.Size.X, Source.Size.Y, SurfaceFormat.Single, samples > 0 ? VisibilityFormat : DepthFormat.None, samples: samples);
        if (Source.Sampling != null) Source.Sampling.ActualSamples = ColorTexture.MultiSampleCount;
        AllocatedChannels = Channels;
        AllocatedSamples = requested;
        AllocatedVisibility = VisibilityFormat;
    }

    /// <inheritdoc/>
    public override void Render()
    {
        if (ColorTexture?.Width != Source.Size.X || ColorTexture.Height != Source.Size.Y || AllocatedChannels != Channels || AllocatedVisibility != VisibilityFormat || AllocatedSamples != (Source.Sampling?.RequestedSamples ?? 0)) { Dispose(); CreateTargets(); }

        MeshCount = 0;

        // The native Vulkan backend resolves a single MSAA attachment per render pass.
        // Keep sampled buffers in independent passes rather than an unsupported MSAA MRT.
        if (ColorTexture.MultiSampleCount > 1)
        {
            Renderer.Reset().SetTarget(ColorTexture).Clear(Color.Transparent);
            DrawGeometry("ColorOnly");

            if (NormalTexture != null) { Renderer.Reset().SetTarget(NormalTexture).Clear(Color.Transparent); DrawGeometry("NormalsOnly"); }

            if (DepthTexture != null) { Renderer.Reset().SetTarget(DepthTexture).Clear(Color.Transparent); DrawGeometry("DepthOnly"); }
        }
        else
        {
            Renderer.Reset();
            if (NormalTexture != null && DepthTexture != null) Renderer.SetTargets(ColorTexture, NormalTexture, DepthTexture);
            else if (NormalTexture != null) Renderer.SetTargets(ColorTexture, NormalTexture);
            else if (DepthTexture != null) Renderer.SetTargets(ColorTexture, DepthTexture);
            else Renderer.SetTarget(ColorTexture);
            Renderer.Clear(Color.Transparent);
            DrawGeometry(NormalTexture != null ? DepthTexture != null ? "Default" : "ColorNormals" : DepthTexture != null ? "ColorDepth" : "ColorOnly");
        }

        MeshCount = Source.HasCamera ? Source.Instances.Count : 0;
        Renderer.Commit().Reset().SetTarget(null);
    }

    /// <summary>Renders one geometry channel using the current view and projection.</summary>
    private void DrawGeometry(string technique)
    {
        Renderer.SetShader("Geometry3D").SetTechnique(technique);
        SubmitMeshes(Renderer);
    }

    /// <summary>Submits geometry for the caller's current render pass.</summary>
    public void Draw(Renderer renderer)
    {
        renderer.SetTechnique("Meshes");
        SubmitMeshes(renderer);
    }

    /// <summary>Uploads and draws visible mesh instances through the renderer.</summary>
    private void SubmitMeshes(Renderer renderer)
    {
        if (!Source.HasCamera) return;

        var camera = Source.Camera;
        renderer.Configure(BlendState.Opaque).Configure(DepthStencilState.Default).SetParameter("View", camera.View).SetParameter("Projection", camera.Projection).SetParameter("FarPlane", camera.FarPlane);

        foreach (var instance in Source.Instances)
        {
            // Reflection reverses triangle winding; preserve the mesh's outward-facing side.
            var rasterizer = instance.DoubleSided ? RasterizerState.CullNone : instance.World.GetDeterminant() < 0 ? RasterizerState.CullCounterClockwise : RasterizerState.CullClockwise;
            renderer.Configure(rasterizer).SetParameter("World", instance.World).SetParameter("NormalMatrix", Matrix.Transpose(MatrixMath.Invert(instance.World))).SetParameter("BaseColor", instance.Color.ToVector4()).DrawMesh(instance.Mesh);
        }
    }

    /// <inheritdoc/>
    public override void OnResize()
    {
        if (ColorTexture?.Width == Source.Size.X && ColorTexture.Height == Source.Size.Y) return;

        Dispose();
        CreateTargets();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Renderer.SetTarget(null);
        if (Source?.Sampling != null) Source.Sampling.ActualSamples = 0;
        ColorTexture?.Dispose();
        NormalTexture?.Dispose();
        DepthTexture?.Dispose();
        ColorTexture = NormalTexture = DepthTexture = null;
    }
}
