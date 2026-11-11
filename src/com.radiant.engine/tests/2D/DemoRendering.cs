using System;
using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>Common image stages and controls for the HRC test scenes.</summary>
public static class DemoRendering
{
    /// <summary>Adds shared color, reconstruction, and presentation stages after HRC lighting.</summary>
    public static Upscaler AddHrc(ECS ecs, HRCGI lighting)
        => AddImage(ecs, lighting, lighting.GetOutput, true);

    /// <summary>Adds image processing, inspector controls, and presentation after an image producer.</summary>
    public static Upscaler AddImage(ECS ecs, System producer, Func<Texture2D> input, bool hrcInput = false)
    {
        var color = ecs.AddSystem(new ToneMapper { Input = input, Operator = ToneMapOperator.None, After = [producer] });
        var aa = ecs.AddSystem(new AntiAliaser { Input = color.GetOutput, Mode = PostAAMode.None, After = [color] });
        var upscaler = new Upscaler { Input = aa.GetOutput, HrcInput = hrcInput, After = [aa] };
        if (hrcInput) upscaler.Select(UpscalerMode.UDR4);
        ecs.AddSystem(upscaler);

        ecs.AddSystem(new ToneMapperSettings { Source = color });
        ecs.AddSystem(new AntiAliaserSettings { Source = aa, Upscaler = upscaler, Title = "Image quality" });

        var pipeline = ecs.AddSystem(new RenderInspector { Input = upscaler.GetOutput, After = [upscaler] });
        pipeline.AddSource("Tone mapping", color);
        pipeline.AddSource("AA", aa);
        pipeline.AddSource("Reconstruction", upscaler);

        ecs.AddSystem(new RenderPresenter { Input = pipeline.GetOutput, ClearColor = Color.Black, Blend = BlendState.Opaque, After = [pipeline] });

        return upscaler;
    }
}
