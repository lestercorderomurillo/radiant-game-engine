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

## Run the radiance example

```bash
dotnet run --project tests/Radiant.Scenes -- radiance
```

The default example has twelve colored lights orbiting close to a nonemissive central triangle and 150 dots distributed across five staggered outer rings.
Left-drag places opaque dots and right-drag erases dots. The twelve orbiting lights are the only emitters.
Press **Space** to pause motion, **R** to reset, **F1** to hide or show the Inspector, or **F11** to cycle reconstruction.
Painting stops over Inspector panels and outside the game viewport. The brush supports up to 4,096 dots.

## Reuse the scene in your project

The complete example has one source of truth:
[`RadianceScene.cs`](../src/com.radiant.engine/tests/2D/RadianceScene.cs).
It uses [`DemoRendering.cs`](../src/com.radiant.engine/tests/2D/DemoRendering.cs) for tone mapping,
anti-aliasing, reconstruction and presentation. Add both sources and the engine's source aliases to the
project above (adjust the repository path as needed):

```xml
<ItemGroup>
  <Compile Include="../radiant-game-engine/eng/Radiant.Numerics.cs" Link="Radiant.Numerics.cs" />
  <Compile Include="../radiant-game-engine/src/com.radiant.engine/tests/2D/RadianceScene.cs" Link="RadianceScene.cs" />
  <Compile Include="../radiant-game-engine/src/com.radiant.engine/tests/2D/DemoRendering.cs" Link="DemoRendering.cs" />
</ItemGroup>
```

A [`Scene`](../src/com.radiant.engine/core/Scene.cs) registers systems in `SetupECS` and creates entities in
`SetupScene`. The engine sorts and initializes the systems between those callbacks. `Update` animates the lights. A brush system runs after Inspector input to handle painting; `Renderer.ScreenToWorld` keeps the brush aligned when the window or docked
viewport changes size. The scene uses **2.7× exposure** and sRGB output.

## Open the window

Create this `Program.cs`:

```csharp
using com.radiant.engine.core;
using com.radiant.engine.runtime;

new GameClient().Run(new RadianceScene(), new WindowOptions { Title = "Radiance", Width = 1280, Height = 720 });
```

`Run` returns when the player closes the window, after cleaning up the scene. The example registers `ECS.AddSystemCollection(SystemCollections.Debugging)`, so all tools open in Panels
mode by default, with Entity Inspector beside Game and Debugger underneath it. Press **F1** to hide or restore the Inspector. Use **…** beside a panel title to move it, drag the divider grips to resize it,
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
