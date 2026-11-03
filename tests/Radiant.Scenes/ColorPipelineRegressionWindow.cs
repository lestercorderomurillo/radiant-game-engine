using System;
using System.Linq;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

internal sealed class ColorPipelineScene : Scene
{
    internal ColorManagement Legacy;
    internal ToneMapping Processor;
    internal UDR4 Upscaler;
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        var lighting = ECS.AddSystem<HRCGI>();
        Legacy = ECS.AddSystem(new ColorManagement { Input = lighting.GetOutput });
        Processor = ECS.AddSystem(new ToneMapping { Input = lighting.GetOutput, After = [Legacy] });
        Upscaler = ECS.AddSystem<UDR4>(); Upscaler.SetInputSource(Legacy.GetOutput);
        base.SetupECS();
    }
    public override void SetupScene()
    {
        Renderer.FitWorld(new Vector2(160, 90), new Vector2(320, 180));
        LightFactory.CreateLight(ECS, new Vector2(160, 90), 40, Color.White, Color.White);
    }
}
internal sealed class ColorPipelineRegressionWindow : Window
{
    private readonly ColorPipelineScene Scene = new();
    private int Frame;
    private readonly ToneMapper[] Cases = Enum.GetValues<ToneMapper>();
    public ColorPipelineRegressionWindow() : base(new GameLoop(), new() { Title = "Shared HRC color pipeline regression", Width = 320, Height = 180 }) { UDRQuality.Set(1); GameLoop.AddScene(Scene); GameLoop.ThrottleUnfocused = false; }
    protected override void Update(GameTime time)
    {
        if (Scene.Legacy != null && Frame % 4 == 0 && Frame / 4 < Cases.Length)
        {
            Scene.Legacy.Mapper = Scene.Processor.Mapper = Cases[Frame / 4];
            Scene.Legacy.Transfer = Scene.Processor.Transfer = ColorTransfer.Gamma22;
            Scene.Legacy.Exposure = Scene.Processor.Exposure = 2;
            Scene.Upscaler.ResetHistory();
        }
        base.Update(time);
    }
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;
        if (Frame % 4 != 0) return;
        var legacy = Scene.Legacy.GetOutput(); var shared = Scene.Processor.GetOutput();
        var left = new Color[legacy.Width * legacy.Height]; var right = new Color[left.Length]; legacy.GetData(left); shared.GetData(right);
        if (!left.SequenceEqual(right) || !left.Any(c => c.R + c.G + c.B > 0)) throw new Exception("HRC Update-phase color does not match shared Render-phase processing.");
        var upscaled = Scene.Upscaler.GetOutput();
        if (upscaled.Format == SurfaceFormat.HalfVector4)
        {
            var pixels = new HalfVector4[upscaled.Width * upscaled.Height]; upscaled.GetData(pixels);
            if (pixels.Any(p => { var v = p.ToVector4(); return !float.IsFinite(v.X + v.Y + v.Z + v.W); })) throw new Exception("Shared color processing broke UDR4 history.");
        }
        if (Scene.Upscaler.HistoryFrames < 2) throw new Exception("UDR4 history did not accumulate after color processing.");
        Console.WriteLine($"PASS: HRC → shared {Scene.Processor.Mapper} / Gamma22 / exposure 2 → UDR4; Update and Render color match.");
        if (Frame == Cases.Length * 4) Exit();
    }
}
