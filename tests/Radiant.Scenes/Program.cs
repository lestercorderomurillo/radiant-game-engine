using System;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Radiant.Testing;
using Microsoft.Xna.Framework;

Scene Scene = args.Length == 0 || args[0] == "lights" ? new SimpleLightScene()
    : args[0] == "tiles" ? new TilesetScene()
    : args[0] == "triangles" ? new MegaLightsScene()
    : throw new ArgumentException("Choose a test scene: lights, tiles, or triangles.");
if (args.Length > 1 && args[1] == "--smoke")
{
    using var Window = new SmokeWindow(Scene, "artifacts/smoke", (ActiveScene, Frame) =>
    {
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
