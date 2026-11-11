# Quickstart

Add Radiant to your C# project, register systems and run your first scene. The same foundation supports games,
simulations and interactive experiences; this guide starts with a small visual example.

## Requirements

You need the .NET 8 SDK (or newer), a Vulkan-capable GPU and a desktop session. On Linux, also install the
Vulkan loader and SDL2.

## Create a game project

A game is its own executable that references the engine. Shaders, fonts and icons are copied to your output
automatically.

The following example shows a minimal game project file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="../radiant-game-engine/eng/Radiant.Desktop.props" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../radiant-game-engine/radiant.csproj" />
  </ItemGroup>
</Project>
```

## Write your first scene

A [`Scene`](../src/com.radiant.engine/core/Scene.cs) does two things: it registers systems in `SetupECS` and
creates entities in `SetupScene`. Both are plain hooks: override only the ones you need and never call
`base`. Between them the engine sorts your systems by their dependencies and initializes them, so everything you
registered is ready when `SetupScene` runs.

The following example shows a scene with 2D global illumination and one warm light in the middle:

```csharp
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.math;

public class MyScene : Scene
{
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        ECS.AddSystem<HRCGI>();
    }

    public override void SetupScene()
    {
        var warm = new Color(255, 180, 100);
        LightFactory.CreateLight(ECS, Renderer.VirtualSize / 2, 100f, warm, warm);
    }
}
```

Register systems in any order; each one declares what it depends on. Vectors are standard
`System.Numerics` types and colors come from `com.radiant.engine.math` (see [math](Math.md)).

## Open the window

The following example shows a complete `Program.cs`:

```csharp
using com.radiant.engine.runtime;

new GameClient().Run(new MyScene(), new WindowOptions { Title = "My game", Width = 1280, Height = 720 });
```

`Run` returns when the player closes the window, after cleaning up the scene. Press **F1** while the game runs
to open the inspector. Choose **View → Workspace Mode → Panels** to keep Entity Inspector beside Game
and Debugger underneath it. Use **…** beside a panel title to move it, drag the divider grips to resize it,
and use Debugger's search and severity dropdown to find messages.

See the [Inspector guide](Inspector.md) for screenshots and a control-by-control walkthrough.

## Next steps

- [ECS architecture](ECS.md): create things and query them fast.
- [Scenes](Scenes.md): build levels in code or from data.
- [Systems](Systems.md): add behavior that runs every frame.
- [Input](Input.md): react to the keyboard and mouse.
- [Global illumination](HRC.md): choose HRC, raster lighting or path tracing.
- [Inspector and gizmos](Inspector.md): build debug panels in a few lines.

Run `eng/docs.sh` to browse this documentation locally, with the API reference generated from the source.
