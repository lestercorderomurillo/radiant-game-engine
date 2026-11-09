<h1 align="center">Radiant</h1>

<p align="center">
  <a href="https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.4.0"><img src="https://img.shields.io/badge/release-v0.4.0-E73C00?style=flat-square" alt="Release v0.4.0"></a>
  <img src="https://img.shields.io/badge/Linux-Vulkan-8B5CF6?style=flat-square" alt="Linux with Vulkan">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8.0">
</p>

[![Radiant — Create something amazing.](https://raw.githubusercontent.com/lestercorderomurillo/radiant-game-engine/v0.4.0/Content/branding/radiant-splash.png)](https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.4.0)

A general-purpose C# engine for games, simulations and interactive experiences.

Build in 2D or 3D, compose the systems your project needs and extend the engine with your own. Radiant brings
together entities, scenes, rendering, input, runtime services and development tools, with MonoGame and Vulkan
powering the desktop backend.

The ambition is a complete toolkit for interactive development, from the first prototype to a finished project.
The capabilities below are available today; the toolkit will continue to grow across the engine.

## Features

- **Entities and components**: plain-struct components, fast parallel queries, spatial lookups and tags.
- **Systems and scenes**: lifecycle callbacks, dependency ordering, pausing and switchable system groups.
- **Composable rendering**: flat, lit or path-traced images in 2D and 3D, with optional hardware ray tracing.
- **2D global illumination**: glowing shapes light the scene, solid shapes cast soft shadows and colored glass
  tints the light, with no light lists or shadow maps.
- **Image quality**: tone mapping, MSAA, SSAA, FXAA, SMAA, TAA, denoising and UDR upscaling.
- **In-game inspector**: debug panels in a few lines, live entity editing and a free-flying camera.
- **Runtime**: keyboard, mouse and text input, frame-rate independent timing, headless simulation and compute
  shaders.

## Get Radiant

**[Download v0.4.0 source](https://github.com/lestercorderomurillo/radiant-game-engine/archive/refs/tags/v0.4.0.zip)** · [Release notes](https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.4.0)

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
        base.SetupECS();
    }

    public override void SetupScene()
    {
        var warm = new Color(255, 180, 100);
        LightFactory.CreateLight(ECS, Renderer.VirtualSize / 2, 100f, warm, warm);
        base.SetupScene();
    }
}
```

Press **F1** in your game to open the inspector.

## Documentation

**[Read the Radiant documentation](https://lestercorderomurillo.github.io/radiant-game-engine/)**: guides for every
feature and the complete C# API reference.

- [Getting started](docs/GettingStarted.md)
- [Entities and components](docs/ECS.md) and [systems and scenes](docs/Systems.md)
- [Input](docs/Input.md) and [inspector](docs/Inspector.md)
- [Renderer](docs/Renderer.md) and [lighting](docs/HRC.md)
- [Rendering pipelines](docs/Rendering.md) and [3D rendering](docs/3D.md)
