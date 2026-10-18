# Radiant

A reusable C# 2D engine: archetype ECS, instanced geometry, radiance-cascade lighting, upscaling, and a runtime inspector. MonoGame **3.8.6 DesktopVK** runs on Linux, Windows, and macOS using the native Vulkan backend.

Pac-Man lives in [raytracing-pacman](https://github.com/lestercorderomurillo/raytracing-pacman). This repository owns the engine, shared shaders/fonts/icons, networking code, and the original lighting and tileset test scenes. It has no dependency on Pac-Man.

## Build and run the test scenes

Install the .NET 8 SDK or newer and a Vulkan-capable graphics driver. A newer installed .NET runtime is supported through `RollForward=Major`. On Linux, use a graphical desktop session with the Vulkan loader and SDL2 dependencies installed. MonoGame supplies the native runtime and shader compiler through NuGet; Wine is not required.

```sh
dotnet build radiant.sln -c Release
dotnet run --project tests/Radiant.Scenes -c Release -- lights
dotnet run --project tests/Radiant.Scenes -c Release -- tiles
```

The first build restores MGCB and compiles every engine shader for DesktopVK. The test scenes compile into their own executable; `radiant.csproj` produces only the engine library. Escape exits.

## Minimal game API

A consumer supplies a scene:

```csharp
using com.radiant.engine.runtime;

new GameClient().Run(new MyScene());
```

A scene registers systems in `SetupECS()` (then calls `base.SetupECS()`), creates entities in `SetupScene()`, and can override update/render hooks. `ECS`, `Renderer`, and `DeltaTime` are available on the scene. Optional window settings are passed as `new WindowOptions { Title = "My game", Width = 1280, Height = 720 }`.

Call `Renderer.FitWorld(center, size)` to keep a world area centered and fully visible as the window resizes, with its proportions preserved. Shapes and world/screen coordinate conversions follow the fitted view automatically; use `Renderer.WorldTransform` for world-space sprites. Gameplay coordinates remain unchanged.

For a sibling game repository, import the desktop build settings and reference the library:

```xml
<Import Project="../radiant-game-engine/eng/Radiant.Desktop.props" />
<ItemGroup>
  <ProjectReference Include="../radiant-game-engine/radiant.csproj" />
</ItemGroup>
```

Games with content add their own `MonoGameContentReference` and local MGCB tool manifest matching the engine version. The engine's compiled shaders, icons, and Inter fonts flow through the project reference into build and publish output. Games own their gameplay and assets; they do not copy the engine source. See the Pac-Man project for a complete consumer.

## Repository boundaries

- `src/com.radiant.engine/{core,bundle,runtime,mplay}`: reusable engine code.
- `Content`: engine shaders, Inter fonts, and inspector icons.
- `src/com.radiant.engine/tests/2D`: preserved `SimpleLightScene` and `TilesetScene`.
- `tests/Radiant.Scenes`: executable test-scene selector.
- `eng/Radiant.Desktop.props`: shared framework, backend, and content-builder settings.

Shader profiles are `vs_6_0` / `ps_6_0`; pixel outputs use `SV_Target`. DesktopVK currently permits one constant buffer per effect. Keep MonoGame packages and `.config/dotnet-tools.json` on the same version.

The window uses MonoGame resize events. Windows timer-resolution calls and GPU performance counters are guarded by OS checks; CPU and memory metrics work across platforms. The game loop owns and disposes the active scene and renderer. Runtime content is resolved relative to the executable, so published apps can start from any working directory.

DesktopVK presentation settings are reapplied after native window initialization so the first frame uses the requested client size. This prevents an initial Vulkan swapchain from presenting only part of the scene before the first resize.

## Validation

```sh
dotnet run --project tests/Radiant.Tests -c Release
dotnet run --project tests/Radiant.Scenes -c Release -- lights --smoke
dotnet run --project tests/Radiant.Scenes -c Release -- tiles --smoke
```

The first command is headless: it checks 2,048 generated tiles, spatial lookups, light initialization, and the portable timer lifecycle. The smoke runs require Vulkan and a display; they render 90 frames, verify nonempty pixels, resize the window, save screenshots under `artifacts/smoke`, and exit. The tileset smoke keeps the original 2,056 × 1,024 × 2 entity world.

Linux CI compiles all shaders and both preserved scenes, then runs the headless checks. Graphical smoke tests are explicit local commands. The migration also fixes stale ECS component references in light/tile creation and makes tile lighting buffers resize safely with positive viewport padding.

## VS Code

Open this folder to load `radiant.sln` automatically through C# Dev Kit. Ctrl+Shift+B builds the library and all test projects; F5 offers the lighting and tileset runners. `.fx` shader files use HLSL language mode, with compilation validated by MGCB during the build. To work on Pac-Man as well, open the sibling game's `raytracing-pacman.code-workspace`; its solution includes all projects from both repositories.
