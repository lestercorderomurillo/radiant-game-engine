using System;
using com.radiant.engine.core;

internal static class GraphicsResourceGpuRegression
{
    internal static void Run(IRenderer renderer, com.radiant.engine.backends.monogame.MonoGameWindow host)
    {
        var originalTitle = renderer.Window.Title;
        renderer.Window.Title = "Radiant portable window regression";
        Require(host.Window.Title == renderer.Window.Title && renderer.Window.ClientBounds.Width == host.Window.ClientBounds.Width
            && renderer.Window.ClientBounds.Height == host.Window.ClientBounds.Height && renderer.Window.IsActive == host.IsActive,
            "Portable window state did not reach the native host");
        renderer.Window.Title = originalTitle;
        var face = renderer.GetFont("Inter", 20);
        Require(ReferenceEquals(face, renderer.GetFont("Inter", 20)) && face.Measure("Radiant").X > 0, "Font face cache/metrics");
        renderer.FontRenderScale = 2;
        Require(Vector2.Distance(renderer.MeasureString("Inter", 20, "Radiant"), renderer.GetFont("Inter", 40).Measure("Radiant") / 2) < 0.001f,
            "Font supersampling metrics");
        renderer.FontRenderScale = 1;
        com.radiant.engine.graphics.FontFace releasedFont;
        using (var fontRenderer = host.CreateRenderer()) releasedFont = fontRenderer.GetFont("Inter", 20);
        try { releasedFont.Measure("disposed"); throw new Exception("Disposed family font accepted"); }
        catch (ObjectDisposedException) { }
        using var image = renderer.CreateTexture(2, 2);
        image.SetData(new[] { Color.Black, Color.White, Color.Red, Color.Lime });
        image.SetData(0, new Rectangle(1, 0, 1, 1), new[] { Color.Coral }, 0, 1);
        var pixel = new Color[1];
        image.GetData(0, new Rectangle(1, 0, 1, 1), pixel, 0, 1);
        Require(pixel[0] == Color.Coral && image.Bounds == new Rectangle(0, 0, 2, 2), "Radiant region upload/readback");
        var sharedAsset = renderer.GetCheckmarkTexture(1);
        Texture2D releasedAsset;
        using (var otherRenderer = host.CreateRenderer())
            releasedAsset = otherRenderer.GetCheckmarkTexture(1);
        Require(releasedAsset.IsDisposed && !sharedAsset.IsDisposed, "Renderer must not destroy another renderer's content texture");
        sharedAsset.GetData(new Color[sharedAsset.Width * sharedAsset.Height]);
        try { image.SetData<Color>(null); throw new Exception("Null pixels accepted"); }
        catch (ArgumentNullException) { }
        var cached = renderer.GetSolidTexture(Color.Coral);
        Require(ReferenceEquals(cached, renderer.GetSolidTexture(Color.Coral)), "Texture cache identity");
        using var target = renderer.CreateRenderTarget(2, 2, SurfaceFormat.Vector4, DepthFormat.Depth16, RenderTargetUsage.PreserveContents);
        Require(target.Format == SurfaceFormat.Vector4 && target.DepthStencilFormat == DepthFormat.Depth16 && target.RenderTargetUsage == RenderTargetUsage.PreserveContents, "Render target description");
        var effect = renderer.GetShaderEffect("ImagePipeline");
        Require(ReferenceEquals(effect, renderer.GetShaderEffect("ImagePipeline")), "Effect cache identity");
        Require(effect.Parameters["NotAParameter"] == null, "Optional parameter lookup");
        var parameter = effect.Parameters["ResolveScale"];
        var technique = effect.Techniques["Resolve"];
        effect.CurrentTechnique = technique;
        parameter.SetValue(Vector2.One);
        renderer.ReleaseShader("ImagePipeline");
        Require(effect.IsDisposed, "Released effect lifetime");
        try { parameter.SetValue(Vector2.One); throw new Exception("Released parameter accepted"); }
        catch (ObjectDisposedException) { }
        var reloaded = renderer.GetShaderEffect("ImagePipeline");
        Require(!reloaded.IsDisposed && !ReferenceEquals(effect, reloaded), "Effect reload must not reuse disposed content");
        try { reloaded.CurrentTechnique = technique; throw new Exception("Foreign technique accepted"); }
        catch (ArgumentException) { }
        renderer.Reset().SetShader("ImagePipeline").SetTechnique("Resolve").SetParameter("ResolveScale", Vector2.One);
        image.Dispose();
        image.Dispose();
        try { renderer.SetParameter("Source", image); throw new Exception("Disposed shader texture accepted"); }
        catch (ObjectDisposedException) { }
        try { renderer.SetTexture(0, image); throw new Exception("Disposed texture binding accepted"); }
        catch (ObjectDisposedException) { }
        try { image.GetData(new Color[4]); throw new Exception("Disposed texture readback accepted"); }
        catch (ObjectDisposedException) { }
        renderer.Reset();
        Console.WriteLine("PASS: Radiant texture regions, caches, target formats, effect reload, technique ownership and resource lifetime.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
