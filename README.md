<h1 align="center">Radiant</h1>

<p align="center">
  <a href="https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.4.2"><img src="https://img.shields.io/badge/release-v0.4.2-E73C00?style=flat-square" alt="Release v0.4.2"></a>
  <img src="https://img.shields.io/badge/Linux-Vulkan-8B5CF6?style=flat-square" alt="Linux with Vulkan">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8.0">
</p>

[![Radiant — Create something amazing.](https://raw.githubusercontent.com/lestercorderomurillo/radiant-game-engine/v0.4.2/Content/branding/radiant-splash.png)](https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.4.2)

A complete C# game engine, from first prototype to final release.

Most engines give you a renderer and leave the rest to you. Radiant takes the opposite approach.

Everything you need ships with the engine and works together from day one. Entities, scenes, rendering, lighting,
input, runtime services and a full suite of development tools are designed as one platform, with a shared
architecture and a consistent API.

Adding a feature to your game never means wiring a stack of unrelated libraries together.

Build in 2D or 3D on the same foundation. Compose the systems your project needs, from physics and animation to
global illumination and path tracing, and extend the engine with your own when the built-in ones are not enough.

The in-game inspector, live entity editing and the free-flying camera are always a key press away. Test, tune and
debug while the game is running.

## Features

- **ECS architecture**: a modern entity-component-system core built for massive worlds.
- **Scenes**: build scenes in code or design them statically, in 2D or 3D.
- **Rendering**: composable pipelines for flat, lit or path-traced images, in 2D and 3D.
- **Global illumination**: real-time path-traced lighting with hardware ray tracing.
- **Tone mapping**: HDR rendering with filmic tone mapping and color management.
- **Anti-aliasing**: MSAA, SSAA, FXAA, SMAA and TAA, selectable per project.
- **Neural denoising**: clean path-traced images at low sample counts.
- **UDR**: Universal Detail Reconstruction, an in-house upscaler for higher frame rates.
- **Spatial lookups**: query by radius, box, position or nearest neighbours at near-zero cost.
- **Automatic dependency resolution**: declare system order once, the engine schedules the rest.
- **System control**: pause, group and switch systems at runtime.
- **Runtime**: keyboard, mouse and text input, fixed timestep, headless mode, compute shaders.
- **Agentic development**: MCP and AI-assisted workflows for iteration and content generation.
- **Inspector**: debug panels in a few lines, live entity editing and a free camera.
- **Asset Studio**: create shaders, sprites and 3D models directly in the engine.
- **Visual graph**: build shaders, materials and gameplay logic with a node-based editor.
- **Blender integration**: move assets between Blender and your project seamlessly.
- **Plugin system**: extend the engine and its tools with your own plugins.

## Get Radiant

**[Download v0.4.2 source](https://github.com/lestercorderomurillo/radiant-game-engine/archive/refs/tags/v0.4.2.zip)** · [Release notes](https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.4.2)

You need the .NET 8 SDK or newer and a Vulkan-capable GPU. On Linux, also install the Vulkan loader and SDL2.

Add Radiant to your game project:

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

Then write a scene and run it. The following example opens a window with one warm light:

```csharp
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.math;
using com.radiant.engine.runtime;

new GameClient().Run(new Campfire(), new WindowOptions { Title = "Campfire" });

public class Campfire : Scene
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

Press **F1** in your game to open the inspector.

## Documentation

**[Read the Radiant documentation](https://lestercorderomurillo.github.io/radiant-game-engine/)**: guides for every
feature and the complete C# API reference.

- [Quickstart](docs/GettingStarted.md)
- [ECS architecture](docs/ECS.md), [scenes](docs/Scenes.md) and [systems](docs/Systems.md)
- [Input](docs/Input.md) and [inspector](docs/Inspector.md)
- [Renderer](docs/Renderer.md) and [lighting](docs/HRC.md)
- [Rendering pipelines](docs/Rendering.md) and [3D rendering](docs/3D.md)
