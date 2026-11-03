using System;
using System.IO;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Radiant.Testing;

if (Array.IndexOf(args, "--capture") >= 0)
{
    using var captureWindow = new ShowcaseCaptureWindow();
    captureWindow.Run();
    return;
}

if (Array.IndexOf(args, "--advanced-denoising-regression") >= 0)
{
    using var advancedDenoisingWindow = new AdvancedDenoisingRegressionWindow(Array.IndexOf(args, "--2d") >= 0);
    advancedDenoisingWindow.Run();
    return;
}

if (Array.IndexOf(args, "--denoising-regression") >= 0)
{
    using var denoisingWindow = new DenoisingRegressionWindow(Array.IndexOf(args, "--2d") >= 0);
    denoisingWindow.Run();
    return;
}

if (Array.IndexOf(args, "--rendering-settings") >= 0)
{
    using var settingsWindow = new RenderingSettingsRegressionWindow(Array.IndexOf(args, "--2d") >= 0);
    settingsWindow.Run();
    return;
}

if (Array.IndexOf(args, "--raster-visibility") >= 0)
{
    using var visibilityWindow = new RasterVisibilityWindow();
    visibilityWindow.Run();
    return;
}

if (Array.IndexOf(args, "--raster-edges") >= 0)
{
    using var edgesWindow = new RasterEdgesWindow();
    edgesWindow.Run();
    return;
}

if (Array.IndexOf(args, "--editor-regression") >= 0)
{
    using var editorWindow = new EditorRegressionWindow(Array.IndexOf(args, "--2d") >= 0, Array.IndexOf(args, "--navigate") >= 0);
    editorWindow.Run();
    return;
}

if (Array.IndexOf(args, "--accumulation-regression") >= 0)
{
    using var accumulationWindow = new AccumulationRegressionWindow();
    accumulationWindow.Run();
    return;
}

if (Array.IndexOf(args, "--aa-regression") >= 0)
{
    using var aaWindow = new AARegressionWindow(Array.IndexOf(args, "--2d") >= 0, Array.IndexOf(args, "--path") >= 0 ? 2 : Array.IndexOf(args, "--raster") >= 0 ? 1 : 0);
    aaWindow.Run();
    return;
}

if (Array.IndexOf(args, "--raster-stability") >= 0)
{
    using var rasterWindow = new RasterStabilityWindow();
    rasterWindow.Run();
    return;
}

if (Array.IndexOf(args, "--mixed-regression") >= 0)
{
    using var mixedWindow = new MixedDimensionWindow();
    mixedWindow.Run();
    return;
}

if (Array.IndexOf(args, "--pipeline-regression") >= 0)
{
    Scene pipelineScene = Array.IndexOf(args, "--2d") >= 0 ? new Rendering2DScene() : new CornellBoxScene();
    using var pipelineWindow = new PipelineRegressionWindow(pipelineScene);
    pipelineWindow.Run();
    return;
}

bool regression = Array.IndexOf(args, "--regression") >= 0;
Scene scene = regression ? new Geometry3DRegressionScene() : new CornellBoxScene { InitialRenderMethod = Array.IndexOf(args, "--smoke") >= 0 ? 0 : 2, InitialDenoiser = Array.IndexOf(args, "--smoke") >= 0 ? DenoiserMode.None : DenoiserMode.Atrous };
if (regression || Array.IndexOf(args, "--smoke") >= 0)
{
    using var window = new SmokeWindow(scene, "artifacts/smoke", (active, frame) =>
    {
        var geometry = active.ECS.GetSystem<Geometry3D>();
        if (frame is 9 or 89)
        {
            if (!regression && (active.ECS.GetSystem<Inspector>() == null ||
                !Inspector.IsWindowVisible("geometry3d") || !Inspector.IsWindowVisible("perf")))
                throw new Exception("Cornell Box framework inspector panels are missing.");
            if (geometry.MeshCount != (regression ? 2 : 8)) throw new Exception("3D meshes were not submitted.");
            if (geometry.ColorTexture.Width != active.Renderer.ScreenWidth ||
                geometry.DepthTexture.Height != active.Renderer.ScreenHeight ||
                geometry.NormalTexture.Height != active.Renderer.ScreenHeight)
                throw new Exception("3D targets did not resize with the window.");
            foreach (var (name, target) in new[] { ("color", geometry.ColorTexture),
                ("normals", geometry.NormalTexture), ("depth", geometry.DepthTexture) })
            {
                Directory.CreateDirectory("artifacts/smoke");
                using var stream = File.Create($"artifacts/smoke/3d-{name}-{frame}.png");
                target.SaveAsPng(stream, target.Width, target.Height);
            }
        }
        if (regression && frame is 9 or 30 or 89)
        {
            int width = geometry.ColorTexture.Width, height = geometry.ColorTexture.Height;
            int center = (height / 2) * width + width / 2;
            var colors = new Color[width * height];
            var normals = new Color[colors.Length];
            var depths = new float[colors.Length];
            geometry.ColorTexture.GetData(colors);
            geometry.NormalTexture.GetData(normals);
            geometry.DepthTexture.GetData(depths);
            var expected = frame == 30 ? Color.OrangeRed : Color.CornflowerBlue;
            if (colors[center] != expected) throw new Exception($"Occlusion/disabled entity failed: {colors[center]}, expected {expected}.");
            float expectedDepth = (frame == 30 ? 6.5f : 4.5f) / 20;
            if (MathF.Abs(depths[center] - expectedDepth) > 0.002f)
                throw new Exception($"Linear depth is incorrect: {depths[center]} instead of {expectedDepth}.");
            if (normals[center].B < 250 || Math.Abs(normals[center].R - 128) > 1 || Math.Abs(normals[center].G - 128) > 1)
                throw new Exception($"World normal encoding is incorrect: {normals[center]}.");
            if (colors[0].A != 0 || depths[0] != 0) throw new Exception("3D buffers retain stale background geometry.");
            Console.WriteLine($"PASS: 3D color, normals, linear depth, occlusion at frame {frame}.");
        }
        if (regression && frame == 20) active.ECS.DisableEntity(((Geometry3DRegressionScene)active).NearCube);
        if (regression && frame == 40) active.ECS.EnableEntity(((Geometry3DRegressionScene)active).NearCube);
        if (regression && frame == 50) geometry.Channels = GeometryChannels.Color;
        if (regression && frame == 55 && (geometry.NormalTexture != null || geometry.DepthTexture != null)) throw new Exception("Color-only geometry allocated optional buffers.");
        if (regression && frame == 60) geometry.Channels = GeometryChannels.Color | GeometryChannels.Normals;
        if (regression && frame == 65 && (geometry.NormalTexture == null || geometry.DepthTexture != null)) throw new Exception("Normal-only MRT selection failed.");
        if (regression && frame == 70) geometry.Channels = GeometryChannels.Color | GeometryChannels.Depth;
        if (regression && frame == 75 && (geometry.NormalTexture != null || geometry.DepthTexture == null)) throw new Exception("Depth-only MRT selection failed.");
        if (regression && frame == 80) geometry.Channels = GeometryChannels.All;
        if (active is CornellBoxScene cornell)
        {
            if (frame == 50) cornell.Pipeline.SelectBuffer(2);
            if (frame == 60) cornell.Pipeline.SelectBuffer(3);
            if (frame == 70) cornell.Pipeline.SelectBuffer(0);
        }
    });
    window.Run();
}
else
    new GameClient().Run(scene, new() { Title = "Radiant — Cornell Box", Maximized = true });
