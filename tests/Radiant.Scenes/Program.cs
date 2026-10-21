using System;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Radiant.Testing;
using Microsoft.Xna.Framework;

if (Array.IndexOf(args, "--lighting-regression") >= 0)
{
    using var window = new LightingRegressionWindow();
    window.Run();
    return;
}

Scene Scene = args.Length == 0 || args[0] == "lights" ? new SimpleLightScene()
    : args[0] == "tiles" ? new TilesetScene()
    : args[0] == "triangles" ? new MegaLightsScene()
    : throw new ArgumentException("Choose a test scene: lights, tiles, or triangles.");
if (args.Length > 1 && args[1] == "--smoke")
{
    using var Window = new SmokeWindow(Scene, "artifacts/smoke", (ActiveScene, Frame) =>
    {
        if (ActiveScene is SimpleLightScene or MegaLightsScene)
        {
            var renderer = ActiveScene.Renderer;
            var center = renderer.VirtualSize / 2;
            var projectedCenter = renderer.WorldToScreen(center);
            float horizontalScale = Vector2.Distance(projectedCenter, renderer.WorldToScreen(center + Vector2.UnitX));
            float verticalScale = Vector2.Distance(projectedCenter, renderer.WorldToScreen(center + Vector2.UnitY));
            if (MathF.Abs(horizontalScale - verticalScale) > 0.001f || Vector2.Distance(projectedCenter, renderer.ScreenSize / 2) > 0.01f)
                throw new Exception("Lighting demo is stretched or off-center.");
            if (Vector2.Distance(renderer.ScreenToWorld(projectedCenter), center) > 0.01f)
                throw new Exception("Demo input mapping does not follow the fitted view.");
        }

        // The world begins with 100 rows of air. Frame the surface for captures
        // so the tiles smoke exercises terrain instead of only the sky clear color.
        if (Frame == 1 && ActiveScene is TilesetScene)
        {
            var World = ActiveScene.ECS.GetSystem<WorldGen>();
            ActiveScene.ECS.GetSystem<Tileset>().CameraPosition =
                new Vector2(0, 100 * World.TileSize - ActiveScene.Renderer.ScreenHeight / 3f);
        }
    });
    Window.Run();
}
else
    new GameClient().Run(Scene, new() { Title = $"Radiant — {Scene.GetType().Name}" });
