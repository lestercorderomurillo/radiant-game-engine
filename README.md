<h1 align="center">Radiant</h1>

<p align="center">
  <a href="https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.7.0"><img src="https://img.shields.io/badge/release-v0.7.0-E73C00?style=flat-square" alt="Release v0.7.0"></a>
  <img src="https://img.shields.io/badge/Linux-Vulkan-8B5CF6?style=flat-square" alt="Linux with Vulkan">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8.0">
</p>

[![Radiant — Create something amazing.](https://raw.githubusercontent.com/lestercorderomurillo/radiant-game-engine/v0.7.0/Content/branding/radiant-splash.png)](https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.7.0)

A C# engine for 2D and 3D games, with an entity-component system, composable rendering pipelines,
global illumination and live inspection tools.

Build a scene in code, run it, and press **F1** to inspect entities, tune rendering or read diagnostics.
The tools can float over the game or dock beside it.

## Features

- **ECS architecture**: a modern entity-component-system core built for massive worlds.
- **Scenes**: build scenes in code or design them statically, in 2D or 3D.
- **Rendering**: composable pipelines for flat, lit or path-traced images, in 2D and 3D.
- **Global illumination**: path-traced lighting with hardware ray tracing and experimental 3D radiance cascades.
- **Tone mapping**: HDR rendering with filmic tone mapping and color management.
- **Anti-aliasing**: MSAA, SSAA, FXAA, SMAA and TAA, selectable per project.
- **Neural denoising**: clean path-traced images at low sample counts.
- **UDR**: Universal Detail Reconstruction, an in-house upscaler for higher frame rates.
- **Spatial lookups**: query by radius, box, position or nearest neighbours at near-zero cost.
- **Automatic dependency resolution**: declare system order once, the engine schedules the rest.
- **System control**: pause, group and switch systems at runtime.
- **Runtime**: keyboard, mouse and text input, fixed timestep, headless mode, compute shaders.
- **Agentic development**: MCP and AI-assisted workflows for iteration and content generation.
- **Inspector**: movable panels, live entity editing, a free camera and searchable diagnostics.
- **Asset Studio**: create shaders, sprites and 3D models directly in the engine.
- **Visual graph**: build shaders, materials and gameplay logic with a node-based editor.
- **Blender integration**: move assets between Blender and your project seamlessly.
- **Plugin system**: extend the engine and its tools with your own plugins.

## Get Radiant

**[Download v0.7.0 source](https://github.com/lestercorderomurillo/radiant-game-engine/archive/refs/tags/v0.7.0.zip)** · [Release notes](https://github.com/lestercorderomurillo/radiant-game-engine/releases/tag/v0.7.0)

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

## Work with the Inspector

Press **F1**, then choose **View → Workspace Mode → Panels**. Entity Inspector opens on the left,
the game stays in the center, and Debugger shows diagnostics below it.

![Radiant Inspector with Entity Inspector beside Game and searchable diagnostics below it](docs/images/inspector-workspace.png)

| Control | What it does |
| --- | --- |
| **…** beside a tab | Move the panel left, right or below Game; reset panel sizes; or close it. |
| **X** | Close that panel. Open it again from **View**. |
| Tab title | Select a panel. Hold and drag it to move it; the highlighted area marks the destination. |
| Divider grip | Resize the adjacent panels. |
| **Clear** | Remove every retained log message, including messages hidden by a filter. |
| **Follow latest** | Keep the newest matching messages in view. Turn it off to read older entries. |
| **Search logs…** | Find text in messages, sources or the displayed timestamp. |
| **All levels** | Show all messages, or only Info, Warnings or Errors. Works together with search. |

Choose **View → Workspace Mode → Windows** for floating tools. Drag a window's title to move it
or its bottom-right corner to resize it. **F1** hides the tools and returns the game to the full window.

[Inspector guide: arrange panels, inspect entities and read logs](docs/Inspector.md)

## Documentation

**[Read the Radiant documentation](https://lestercorderomurillo.github.io/radiant-game-engine/)**: guides for every
feature and the complete C# API reference.

- [Quickstart](docs/GettingStarted.md)
- [ECS architecture](docs/ECS.md), [scenes](docs/Scenes.md) and [systems](docs/Systems.md)
- [Input](docs/Input.md) and [inspector](docs/Inspector.md)
- [Renderer](docs/Renderer.md) and [lighting](docs/HRC.md)
- [Rendering pipelines](docs/Rendering.md) and [3D rendering](docs/3D.md)
