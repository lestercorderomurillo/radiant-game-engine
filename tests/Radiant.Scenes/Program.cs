using System;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Radiant.Testing;

Scene Scene = args.Length == 0 || args[0] == "lights" ? new SimpleLightScene()
    : args[0] == "tiles" ? new TilesetScene()
    : throw new ArgumentException("Choose a test scene: lights or tiles.");
if (args.Length > 1 && args[1] == "--smoke")
{
    using var Window = new SmokeWindow(Scene, "artifacts/smoke");
    Window.Run();
}
else
    new GameClient().Run(Scene, new() { Title = $"Radiant — {Scene.GetType().Name}" });
