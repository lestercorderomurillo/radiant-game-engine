# Getting started

Build the engine, try a demo, then open a window with your own lit scene in about twenty lines of code.

## Run a demo

You need the .NET 8 SDK (or newer), a Vulkan-capable GPU and a desktop session. On Linux, also install the
Vulkan loader and SDL2.

The following example shows how to build everything and launch the simple lighting demo:

```sh
dotnet build radiant.sln -c Release
dotnet run --project tests/Radiant.Scenes -c Release --no-build -- lights
```

Swap `lights` for `triangles` (MegaLights), `tiles` or `rendering2d`, or run the 3D Cornell Box with
`dotnet run --project tests/Radiant.3D -c Release`. Press **F1** in any demo to open the inspector.

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
creates entities in `SetupScene`.

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

Register systems in any order; each one declares what it depends on. Vectors are standard
`System.Numerics` types and colors come from `com.radiant.engine.math` (see [math](Math.md)).

## Open the window

The following example shows a complete `Program.cs`:

```csharp
using com.radiant.engine.runtime;

new GameClient().Run(new MyScene(), new WindowOptions { Title = "My game", Width = 1280, Height = 720 });
```

`Run` returns when the player closes the window, after cleaning up the scene.

## Next steps

- [Entities and components](ECS.md): create things and query them fast.
- [Systems and scenes](Systems.md): add behavior that runs every frame.
- [Input](Input.md): react to the keyboard and mouse.
- [2D global illumination](HRC.md): lights, shadows and colored glass.
- [Inspector and gizmos](Inspector.md): build debug panels in a few lines.

Run `eng/docs.sh` to browse this documentation locally, with the API reference generated from the source.
