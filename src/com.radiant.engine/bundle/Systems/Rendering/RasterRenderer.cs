using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Diffuse geometry lighting before MSAA resolve, or an image lighting pass with optional normals.</summary>
[SystemTag("Rendering")]
public class RasterRenderer : ImageSystem
{
    /// <summary>Optional geometry path; lights fragments before coverage resolve. Input remains the disabled-stage fallback.</summary>
    public IRasterGeometry Geometry;
    /// <summary>Normal texture for image lighting when Geometry is not supplied.</summary>
    public Func<Texture2D> Normals;
    public Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.4f, 0.8f, 0.6f));
    public Vector3 LightColor = Vector3.One;
    public float Ambient = 0.12f;

    /// <inheritdoc/>
    public override void Render()
    {
        if (Geometry != null) { RenderGeometry(); return; }

        var input = Input?.Invoke();

        if (input == null) { Output?.Dispose(); Output = null; return; }

        if (Output != null && (Output.MultiSampleCount > 0 || Output.DepthStencilFormat != DepthFormat.None)) { Renderer.SetTarget(null); Output.Dispose(); Output = null; }

        EnsureOutput(input, SurfaceFormat.HalfVector4);

        var normals = Normals?.Invoke();
        Renderer.Reset().SetShader("ImagePipeline").SetTechnique("Raster").Configure(SamplerState.PointClamp).SetTarget(Output).SetParameter("Source", input).SetParameter("Normals", normals ?? input).SetParameter("HasNormals", normals != null).SetParameter("LightDirection", Vector3.Normalize(LightDirection)).SetParameter("LightColor", LightColor).SetParameter("Ambient", Ambient).Draw().Commit().SetTarget(null);
    }

    /// <summary>Shades covered geometry samples directly into the raster lighting target.</summary>
    private void RenderGeometry()
    {
        var size = Geometry.Size;
        int samples = Renderer.GetSupportedSamples(Geometry.RequestedSamples, SurfaceFormat.HalfVector4, Geometry.VisibilityFormat);

        if (Output?.Width != size.X || Output.Height != size.Y || Output.Format != SurfaceFormat.HalfVector4 || Output.MultiSampleCount != samples || Output.DepthStencilFormat != Geometry.VisibilityFormat)
        {
            Renderer.SetTarget(null);
            Output?.Dispose();
            Output = Renderer.CreateRenderTarget(size.X, size.Y, SurfaceFormat.HalfVector4, Geometry.VisibilityFormat, samples: samples);
        }

        Renderer.Reset().SetShader("RasterLighting").SetTarget(Output).Clear(Color.Transparent).SetParameter("LightDirection", Vector3.Normalize(LightDirection)).SetParameter("LightColor", LightColor).SetParameter("Ambient", Ambient);
        Geometry.Draw(Renderer);
        Renderer.Commit().SetTarget(null);
    }
}
