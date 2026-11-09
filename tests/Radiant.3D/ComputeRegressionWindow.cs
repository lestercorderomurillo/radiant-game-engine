using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.IO;
using System.Runtime.InteropServices;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

internal sealed class ComputeRegressionWindow : Window
{
    private IRenderer Renderer;
    private IComputeShader Kernel;
    private RenderTarget2D Target;
    private int Frame;
    private readonly bool ExpectUnavailable;

    public ComputeRegressionWindow(bool ExpectUnavailable) : base(new GameLoop(), new() { Title = "Compute regression", Width = 160, Height = 120 })
    {
        this.ExpectUnavailable = ExpectUnavailable;
    }

    protected override void Draw(GameTime Time)
    {
        Renderer ??= CreateRenderer();
        bool supported = Renderer.SupportsComputeShaders(out var reason);
        if (ExpectUnavailable)
        {
            if (supported) throw new Exception("Stock runtime unexpectedly supports compute.");
            try { Renderer.CreateComputeShader([], 65, 49); throw new Exception("Unsupported compute accepted."); }
            catch (NotSupportedException) { }
            Console.WriteLine("PASS: stock runtime rejects compute cleanly: " + reason);
            Exit();
            return;
        }
        if (!supported) throw new Exception(reason);
        if (Kernel == null)
        {
            Kernel = Renderer.CreateComputeShader(File.ReadAllBytes("artifacts/compute/Fill.spv"), 65, 49, 16);
            Target?.Dispose();
            Target = Renderer.CreateRenderTarget(65, 49, SurfaceFormat.Vector4);
            try { Kernel.Dispatch(1, 1); throw new Exception("Invalid constants accepted."); } catch (ArgumentException) { }
            try { Kernel.Dispatch(0, 1); throw new Exception("Invalid groups accepted."); } catch (ArgumentOutOfRangeException) { }
        }
        Vector4 color = new(0.1f + Frame * 0.01f, 0.2f, 0.3f, 1);
        Kernel.Dispatch(9, 7, Constants: MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref color, 1)));
        Vector4 add = new(0.125f, 0.25f, 0.5f, -1);
        Kernel.Dispatch(9, 7, Constants: MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref add, 1)));
        Renderer.Reset().SetTarget(Target);
        if (Frame % 2 == 0) Renderer.DrawTexture(Kernel.Output, Vector2.Zero).Commit();
        else Renderer.SetShader("ImagePipeline").SetTechnique("Resolve")
            .SetParameter("Source", Kernel.Output).SetParameter("ResolveScale", Vector2.One).Draw().Commit();
        Renderer.SetTarget(null);
        var pixels = new Vector4[65 * 49];
        Kernel.Synchronize();
        Target.GetData(pixels);

        var expected = color + new Vector4(add.X, add.Y, add.Z, 0);
        foreach (var pixel in pixels)
            if (Vector4.Distance(pixel, expected) > 0.0001f) throw new Exception($"Compute/raster mismatch: {pixel}, expected {expected}");
        if (++Frame == 4)
        {
            Kernel.Dispose();
            try { Kernel.Dispatch(1, 1); throw new Exception("Disposed dispatch accepted."); } catch (ObjectDisposedException) { }
            Kernel = null;
        }
        if (Frame == 8)
        {
            Kernel.Dispose();
            Kernel = null;
            Console.WriteLine("PASS: compute write/read/write, push constants, partial groups, raster sampling, eight frames and recreation.");
            Exit();
        }
    }

    protected override void UnloadContent()
    {
        Kernel?.Dispose();
        Target?.Dispose();
        Renderer?.Dispose();
        base.UnloadContent();
    }
}
