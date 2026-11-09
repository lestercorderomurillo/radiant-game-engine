using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Reconstructs reduced-resolution diffuse lighting on full-resolution primary surfaces, after denoising.</summary>
[SystemTag("Rendering", "Path lighting")]
public class PathLighting : ImageSystem
{
    public PathTracer Source;
    public RenderTarget2D DepthTexture => Surfaces[2];

    private readonly RenderTarget2D[] Surfaces = new RenderTarget2D[4];
    private TraceCamera PreviousCamera;
    private long PreviousRevision = -1;
    private Texture2D PreviousScene;
    private bool HadCamera;

    /// <summary>Registers the full-resolution surface guides used for lighting reconstruction.</summary>
    public PathLighting()
    {
        BufferProviders["Albedo"] = () => Surfaces[0];
        BufferProviders["Normals"] = () => Surfaces[1];
        BufferProviders["Depth"] = () => Surfaces[2];
        BufferProviders["Emission"] = () => Surfaces[3];
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public override Texture2D GetOutput() => Enabled && Output != null ? Output : Input?.Invoke();

    /// <inheritdoc/>
    public override void Render()
    {
        if (Source == null) throw new InvalidOperationException("PathLighting requires a PathTracer source.");

        var input = Input?.Invoke();

        if (!Source.Enabled || !Source.SeparateLighting || input == null) { Dispose(); return; }
        if (Source.AlbedoTexture == null || Source.NormalTexture == null || Source.DepthTexture == null || Source.EmissionTexture == null)
            throw new InvalidOperationException("PathLighting requires albedo, normal, depth and emission trace channels.");

        var size = Source.ViewSize?.Invoke() ?? Source.Geometry.Size;
        bool dirty = Output?.Width != size.X || Output.Height != size.Y;

        if (dirty)
        {
            Dispose();
            Output = Renderer.CreateRenderTarget(size.X, size.Y, SurfaceFormat.Vector4);

            for (int channel = 0; channel < Surfaces.Length; channel++)
                Surfaces[channel] = Renderer.CreateRenderTarget(size.X, size.Y, channel == 2 ? SurfaceFormat.Single : channel == 3 ? SurfaceFormat.HalfVector4 : SurfaceFormat.Color, usage: RenderTargetUsage.PreserveContents);
        }

        var camera = Source.Geometry.Camera;
        var scene = Source.Buffers["BVH data"]();
        dirty |= camera != PreviousCamera || Source.Geometry.Revision != PreviousRevision || !ReferenceEquals(scene, PreviousScene) || HadCamera != Source.Geometry.HasCamera;

        if (dirty)
        {
            for (int channel = 0; channel < Surfaces.Length; channel++)
            {
                Renderer.Reset().SetTarget(Surfaces[channel]).Clear(Color.Transparent);
                Source.DrawSurface(channel);
                Renderer.SetTarget(null);
            }

            PreviousCamera = camera;
            PreviousRevision = Source.Geometry.Revision;
            PreviousScene = scene;
            HadCamera = Source.Geometry.HasCamera;
        }

        Renderer.Reset().SetTarget(Output).SetShader("PathLighting").Configure(SamplerState.PointClamp, 0)
            .SetParameter("Source", input).SetParameter("Albedo", Source.AlbedoTexture).SetParameter("Normals", Source.NormalTexture)
            .SetParameter("Depth", Source.DepthTexture).SetParameter("Emission", Source.EmissionTexture)
            .SetParameter("FullAlbedo", Surfaces[0]).SetParameter("FullNormals", Surfaces[1]).SetParameter("FullDepth", Surfaces[2]).SetParameter("FullEmission", Surfaces[3])
            .SetParameter("LightingSize", new Vector2(input.Width, input.Height)).SetParameter("Environment", Source.Environment)
            .SetParameter("NativeResolution", input.Width == size.X && input.Height == size.Y)
            .SetParameter("HasCamera", Source.Geometry.HasCamera).Draw().Commit().SetTarget(null);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Renderer.SetTarget(null);
        base.Dispose();

        for (int channel = 0; channel < Surfaces.Length; channel++) { Surfaces[channel]?.Dispose(); Surfaces[channel] = null; }

        PreviousRevision = -1;
        PreviousScene = null;
    }
}
