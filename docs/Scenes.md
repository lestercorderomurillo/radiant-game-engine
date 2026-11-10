# Scenes

A [`Scene`](../src/com.radiant.engine/core/Scene.cs) is a level, a menu or any other screen: it registers the
systems it needs and creates its entities, and the engine runs it until you switch to another one.

## Build a scene in code

A scene registers systems in `SetupECS` and creates content in `SetupScene`. The engine sorts and initializes the
systems between the two, so every registered system is ready when `SetupScene` runs. Both are plain hooks:
override only the ones you need and never call `base`.

The following example shows a scene that spawns a player and toggles pause with P:

```csharp
public class Arena : Scene
{
    public override void SetupECS()
    {
        ECS.AddSystem<Movement2DSystem>();
        ECS.AddSystem<Spinner>();
    }

    public override void SetupScene()
    {
        int player = ECS.CreateEntity(new Vector2(400, 300));
        ECS.AddComponent<Movement2D>(player);
    }

    public override void Update()
    {
        if (Controls.WasPressed(Key.P)) ECS.GameplayPaused = !ECS.GameplayPaused;
    }
}
```

A scene's own `Update`, `FixedUpdate`, `Render` and `LateRender` run just before the systems' callbacks of the
same name, which makes them the place for scene-wide logic.

## Design a scene statically

Content that does not change at runtime can be declared up front and spawned in one call. Factories such as
[`LightFactory`](../src/com.radiant.engine/bundle/Extensions/LightFactory.cs) and the tileset and world
generators build whole layouts from data.

The following example shows a static layout of lights built from a list:

```csharp
static readonly (Vector2 Position, Color Color)[] Lamps =
[
    (new Vector2(300, 200), new Color(255, 180, 100)),
    (new Vector2(900, 200), new Color(100, 180, 255)),
    (new Vector2(600, 600), new Color(180, 255, 120)),
];

public override void SetupScene()
{
    foreach (var (position, color) in Lamps)
        LightFactory.CreateLight(ECS, position, 60f, color, color);
}
```

## Run a scene

The following example shows how to open a window on a scene:

```csharp
new GameClient().Run(new Arena(), new WindowOptions { Title = "Arena", Width = 1280, Height = 720 });
```

`Run` returns when the player closes the window, after disposing the scene. See [Runtime](RuntimeBackends.md)
for headless execution and scene transitions.

## 2D and 3D

The same scene type hosts both. Create entities with a `Vector2` for 2D (a `Transform2D` with a draw layer) or a
`Vector3` for 3D (a `Transform3D`), and register the matching producers: `Geometry` and `HRCGI` for lit 2D,
`MeshGeometry3D` and `Geometry3D` or `PathTracer` for 3D. See [2D and 3D scenes](3D.md) for the 3D setup.

## Continue

- [ECS architecture](ECS.md): entities, components and queries.
- [Systems](Systems.md): behavior that runs every frame.
- [Rendering pipelines](Rendering.md): compose the image a scene shows.
