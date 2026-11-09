using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

internal sealed class RenderStateRegressionWindow : Window
{
    public RenderStateRegressionWindow() : base(new GameLoop(), new() { ShowLoader = false, Title = "Radiant render states", Width = 160, Height = 120 }) { }

    private IRenderer Renderer;
    private Texture2D Source, Pattern;
    private RenderTarget2D Target;
    private int Frame;
    private readonly Color Background = new(20, 40, 60, 255);
    private readonly Color Foreground = new(64, 128, 192, 128);
    private readonly Color[] Colors = { new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), Color.White };
    private readonly BlendState[] Blends =
    {
        BlendState.Opaque, BlendState.NonPremultiplied, BlendState.AlphaBlend,
        new() { ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.InverseSourceAlpha },
        BlendState.Opaque with { ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Alpha }
    };
    private readonly Color[] Expected = { new(64,128,192,128), new(42,84,126,191), new(74,148,222,255), new(10,20,30,127), new(64,40,60,128) };

    protected override void Draw(GameTime time)
    {
        if (Renderer == null)
        {
            Renderer = CreateRenderer();
            GraphicsResourceGpuRegression.Run(Renderer, this);
            try { Renderer.Configure((object)Microsoft.Xna.Framework.Graphics.BlendState.Opaque); throw new Exception("Native state silently accepted"); }
            catch (ArgumentException) { }
            Source = Renderer.CreateTexture(1, 1);
            Source.SetData(new[] { Foreground });
            Pattern = Renderer.CreateTexture(2, 2);
            Pattern.SetData(Colors);
            Target = Renderer.CreateRenderTarget(4, 4, SurfaceFormat.Color);
        }
        int scenario = Frame / 3;
        // Read after presentation: Native Vulkan submits the preceding frame asynchronously.
        if (Frame % 3 == 2)
        {
            var pixels = new Color[16];
            Target.GetData(pixels);
            if (scenario < 5)
                foreach (var pixel in pixels) Equal(pixel, Expected[scenario], "Blend case " + scenario);
            else if (scenario < 9)
            {
                var flip = (SpriteEffects)(scenario - 5);
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                {
                    int sx = x / 2, sy = y / 2;
                    if ((flip & SpriteEffects.FlipHorizontally) != 0) sx = 1 - sx;
                    if ((flip & SpriteEffects.FlipVertically) != 0) sy = 1 - sy;
                    Equal(pixels[y * 4 + x], Colors[sy * 2 + sx], "Point sampling / " + flip);
                }
            }
            else if (scenario == 9) Equal(pixels[5], new Color(159, 64, 64), "Linear filtering");
            else foreach (var pixel in pixels) Equal(pixel, scenario is 11 or 13 ? Color.Coral : Color.Red, "Shape batch case " + scenario);
            if (scenario == 13)
            {
                Console.WriteLine("PASS: Radiant blend descriptions, custom mask subtraction, channel masks, point/linear sampling, sprite flips, external/main batch growth, Z order and empty clears on GPU.");
                Exit();
                return;
            }
        }
        Renderer.Reset().SetTarget(Target).Clear(Background).Configure(SamplerState.PointClamp);
        if (scenario < 5)
            Renderer.Configure(Blends[scenario]).DrawTexture(Source, new Rectangle(0, 0, 4, 4));
        else if (scenario < 9)
            Renderer.DrawTexture(Pattern, Vector2.Zero, null, Color.White, 0, Vector2.Zero, new Vector2(2), (SpriteEffects)(scenario - 5), 0);
        else if (scenario == 9) Renderer.Configure(SamplerState.LinearClamp).DrawTexture(Pattern, new Rectangle(0, 0, 4, 4));
        else if (scenario == 10)
        {
            var external = new Shape[65537];
            Array.Fill(external, Shape.Rect(Vector2.Zero, Renderer.VirtualSize, Color.Red));
            Renderer.FlushShapesExternal(external, external.Length, Target, technique: "Sharp");
        }
        else if (scenario == 11)
        {
            for (int i = 0; i < 65537; i++) Renderer.DrawRect(Vector2.Zero, Renderer.VirtualSize, Color.Coral);
            Renderer.FlushShapes(Target, technique: "Sharp");
        }
        else if (scenario == 12)
        {
            Renderer.InitializeParallelShapes(1, 3);
            var shapes = Renderer.GetParallelBuffer(0);
            var depths = Renderer.GetParallelZBuffer(0);
            shapes[0] = Shape.Rect(Vector2.Zero, Renderer.VirtualSize, Color.Red); depths[0] = 2;
            shapes[1] = Shape.Rect(Vector2.Zero, Renderer.VirtualSize, Color.Lime); depths[1] = 0;
            shapes[2] = Shape.Rect(Vector2.Zero, Renderer.VirtualSize, Color.White); depths[2] = 1;
            Renderer.SetParallelCount(0, 3);
            Renderer.SortParallelBufferByZ(0);
            Renderer.CollectParallelShapesSorted().FlushShapes(Target, technique: "Sharp");
        }
        else Renderer.ClearShapes().FlushShapes(Target, Color.Coral, "Sharp");
        Renderer.Commit().SetTarget(null);
        Frame++;
    }

    protected override void UnloadContent()
    {
        Target?.Dispose();
        Source?.Dispose();
        Pattern?.Dispose();
        Renderer?.Dispose();
        base.UnloadContent();
    }
    private static void Equal(Color actual, Color expected, string label)
    {
        if (Math.Abs(actual.R - expected.R) > 1 || Math.Abs(actual.G - expected.G) > 1 || Math.Abs(actual.B - expected.B) > 1 || Math.Abs(actual.A - expected.A) > 1)
            throw new Exception($"{label}: {actual} != {expected}");
    }
}
