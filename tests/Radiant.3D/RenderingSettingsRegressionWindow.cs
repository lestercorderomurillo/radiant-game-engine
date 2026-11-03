using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

internal sealed class RenderingSettingsRegressionWindow : Window
{
    private readonly Scene Scene;
    private readonly bool TwoD;
    private int Frame;
    private Texture2D InputTexture;
    private ColorManagement Legacy;
    private Func<Texture2D> OriginalInput;
    private readonly List<(ToneMapper Mapper, ColorTransfer Transfer, float Exposure)> Cases = new();
    private readonly Vector4[] Samples = [Vector4.Zero, new(0.18f, 0.18f, 0.18f, 1), new(0.09f, 0.09f, 0.09f, 0.5f), new(4, 2, 1, 1), new(1, 0.5f, 0.25f, 0.25f), new(0.0001f, 0.0001f, 0.0001f, 1)];
    private readonly HashSet<string> Curves = new();
    private ToneMapping Tone => Scene is CornellBoxScene c ? c.ToneMap : ((Rendering2DScene)Scene).ToneMap;
    private AntiAliasing AA => Scene is CornellBoxScene c ? c.AA : ((Rendering2DScene)Scene).AA;
    private string Prefix => TwoD ? "rendering2d" : "geometry3d";
    private Dictionary<string, WindowData> Windows => (Dictionary<string, WindowData>)typeof(Inspector).GetField("Windows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Scene.ECS.GetSystem<Inspector>());
    private Widget Widget(string id, string field) => Windows[id].Widgets[Windows[id].WidgetIndex[field]];
    public RenderingSettingsRegressionWindow(bool twoD) : base(new GameLoop(), new() { Title = "Shared rendering settings regression", Width = 1280, Height = 720 })
    {
        TwoD = twoD; Scene = twoD ? new Rendering2DScene() : new CornellBoxScene();
        foreach (var mapper in Enum.GetValues<ToneMapper>()) foreach (var transfer in Enum.GetValues<ColorTransfer>()) Cases.Add((mapper, transfer, 1));
        Cases.Add((ToneMapper.None, ColorTransfer.Linear, 2)); Cases.Add((ToneMapper.None, ColorTransfer.Linear, 1));
        GameLoop.AddScene(Scene); GameLoop.ThrottleUnfocused = false;
    }
    protected override void Update(GameTime time)
    {
        base.Update(time);
        if (InputTexture == null)
        {
            InputTexture = Scene.Renderer.CreateTexture(Samples.Length, 1, SurfaceFormat.Vector4); InputTexture.SetData(Samples);
            OriginalInput = Tone.Input; Tone.Input = () => InputTexture;
            Legacy = new ColorManagement { Renderer = Scene.Renderer, Scene = Scene, SettingsId = "legacyColor" }; Legacy.SetInputSource(() => InputTexture); Legacy.Initialize();
            if (Widget(Prefix + "Color", "tonemap").DropdownOptions.Length != Enum.GetValues<ToneMapper>().Length) throw new Exception("Shared window omits color operators.");
            if (Windows[Prefix].WidgetIndex.ContainsKey("tonemap") || Windows[Prefix].WidgetIndex.ContainsKey("geometryAA")) throw new Exception("Scene window still owns shared rendering controls.");
            Widget(Prefix + "AA", "postAA").DropdownCallback((int)PostAAMode.TAA);
            if (AA.Mode != PostAAMode.TAA) throw new Exception("Shared AA controls do not edit the bound view.");
            Widget(Prefix + "AA", "postAA").DropdownCallback((int)PostAAMode.None);
        }
        if (Frame < Cases.Count)
        {
            var test = Cases[Frame];
            Widget(Prefix + "Color", "tonemap").DropdownCallback((int)test.Mapper);
            Widget(Prefix + "Color", "transfer").DropdownCallback((int)test.Transfer);
            Widget(Prefix + "Color", "exposure").SliderCallback(test.Exposure);
            Legacy.Mapper = test.Mapper; Legacy.Transfer = test.Transfer; Legacy.Exposure = test.Exposure; Legacy.Update();
        }
        else if (Frame == Cases.Count)
        {
            Widget(Prefix + "Color", "enabled").ToggleCallback(false);
            if (Tone.Enabled || Tone.GetOutput() != InputTexture) throw new Exception("Disabled color processing does not bypass its input.");
            Widget(Prefix + "Color", "enabled").ToggleCallback(true);
            Tone.Mapper = ToneMapper.AgX; Tone.Transfer = ColorTransfer.SRGB; Tone.Input = () => null;
            Legacy.Dispose(); Legacy = null;
        }
        else
        {
            Tone.Input = OriginalInput;
            foreach (string id in Windows.Keys.ToArray()) Inspector.HideWindow(id);
            Inspector.ShowWindow(Prefix + "Color"); Inspector.ShowWindow(Prefix + "AA");
            typeof(Inspector).GetField("GlobalVisible", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Scene.ECS.GetSystem<Inspector>(), true);
        }
    }
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        if (Frame < Cases.Count)
        {
            var test = Cases[Frame];
            var output = Tone.GetOutput(); var pixels = Read(output); var legacy = Read(Legacy.GetOutput());
            if (!pixels.SequenceEqual(legacy)) throw new Exception("HRC update-phase adapter differs from shared render-phase color processing.");
            if (pixels.Any(p => !float.IsFinite(p.X + p.Y + p.Z + p.W))) throw new Exception("Color processing produced non-finite HDR pixels.");
            if (pixels[0] != Vector4.Zero) throw new Exception("Color processing changed transparent background.");
            if (Vector4.Distance(pixels[1] * 0.5f, pixels[2]) > 0.006f) throw new Exception("Color processing does not unpremultiply before nonlinear operations.");
            for (int i = 0; i < pixels.Length; i++) if (Math.Abs(pixels[i].W - Samples[i].W) > 0.004f) throw new Exception("Color processing changed alpha coverage.");
            if (test.Mapper == ToneMapper.None || test.Mapper == ToneMapper.Reinhard)
                for (int i = 1; i < pixels.Length; i++)
                {
                    float x = Samples[i].X / Samples[i].W * test.Exposure;
                    if (test.Mapper == ToneMapper.Reinhard) x /= 1 + x;
                    if (test.Transfer == ColorTransfer.SRGB) x = x <= 0.0031308f ? 12.92f * x : 1.055f * MathF.Pow(x, 1 / 2.4f) - 0.055f;
                    if (test.Transfer == ColorTransfer.Gamma22) x = MathF.Pow(x, 1 / 2.2f);
                    x *= Samples[i].W;
                    if (output.Format == SurfaceFormat.Color) x = Math.Clamp(x, 0, 1);
                    if (Math.Abs(pixels[i].X - x) > Math.Max(0.004f, x * 0.001f)) throw new Exception("Exposure/transfer/alpha operation order is incorrect.");
                }
            if (test.Transfer == ColorTransfer.Linear && test.Exposure == 1) Curves.Add(string.Join(",", pixels.Select(p => p.ToString())));
            if (test.Mapper == ToneMapper.None && test.Transfer == ColorTransfer.Linear && test.Exposure == 1)
            {
                if (output != InputTexture || typeof(ImageSystem).GetField("Output", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Tone) != null) throw new Exception("Identity color processing retained a render target.");
            }
            Console.WriteLine($"PASS: {(TwoD ? "2d" : "3d")} shared color {test.Mapper}, {test.Transfer}, exposure={test.Exposure}; alpha and legacy adapter match.");
        }
        else if (Frame == Cases.Count)
        {
            if (Tone.GetOutput() != null) throw new Exception("Missing color input retained stale output.");
            if (Curves.Count != Enum.GetValues<ToneMapper>().Length) throw new Exception("Color operator selection produced duplicate curves.");
        }
        else
        {
            if (Widget(Prefix + "Color", "tonemap").DropdownSelected != (int)Tone.Mapper || Widget(Prefix + "Color", "transfer").DropdownSelected != (int)Tone.Transfer) throw new Exception("Color settings do not track programmatic edits.");
            Directory.CreateDirectory("artifacts/rendering-settings");
            var target = Scene.Renderer.SceneRT;
            using (var file = File.Create($"artifacts/rendering-settings/{(TwoD ? "2d" : "3d")}.png")) target.SaveAsPng(file, target.Width, target.Height);
            InputTexture.Dispose();
            Console.WriteLine("PASS: shared color/AA windows, disabled and identity bypass, missing-input cleanup, programmatic sync and distinct operators.");
            Exit();
        }
        Frame++;
    }
    private static Vector4[] Read(Texture2D texture)
    {
        if (texture.Format == SurfaceFormat.Vector4) { var data = new Vector4[texture.Width * texture.Height]; texture.GetData(data); return data; }
        if (texture.Format == SurfaceFormat.HalfVector4) { var data = new HalfVector4[texture.Width * texture.Height]; texture.GetData(data); return data.Select(p => p.ToVector4()).ToArray(); }
        var colors = new Color[texture.Width * texture.Height]; texture.GetData(colors); return colors.Select(p => p.ToVector4()).ToArray();
    }
}
