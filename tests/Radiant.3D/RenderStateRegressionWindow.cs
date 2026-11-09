using System;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

internal sealed class RenderStateRegressionWindow : Window
{
    public RenderStateRegressionWindow() : base(new GameLoop(), new() { Title = "Radiant render states", Width = 160, Height = 120 }) { }

    private Renderer Renderer;
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
            Renderer = new Renderer(this);
            try { Renderer.Configure((object)Microsoft.Xna.Framework.Graphics.BlendState.Opaque); throw new Exception("Native state silently accepted"); }
            catch (ArgumentException) { }
            Source = new Texture2D(GraphicsDevice, 1, 1);
            Source.SetData(new[] { Foreground });
            Pattern = new Texture2D(GraphicsDevice, 2, 2);
            Pattern.SetData(Colors);
            Target = new RenderTarget2D(GraphicsDevice, 4, 4, false, SurfaceFormat.Color, DepthFormat.None);
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
            else Equal(pixels[5], new Color(159, 64, 64), "Linear filtering");
            if (scenario == 9)
            {
                Console.WriteLine("PASS: Radiant blend descriptions, custom mask subtraction, channel masks, point/linear sampling and sprite flip flags on GPU.");
                Exit();
                return;
            }
        }
        Renderer.Reset().SetTarget(Target).Clear(Background).Configure(SamplerState.PointClamp);
        if (scenario < 5)
            Renderer.Configure(Blends[scenario]).DrawTexture(Source, new Rectangle(0, 0, 4, 4));
        else if (scenario < 9)
            Renderer.DrawTexture(Pattern, Vector2.Zero, null, Color.White, 0, Vector2.Zero, new Vector2(2), (SpriteEffects)(scenario - 5), 0);
        else Renderer.Configure(SamplerState.LinearClamp).DrawTexture(Pattern, new Rectangle(0, 0, 4, 4));
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
