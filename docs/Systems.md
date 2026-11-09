# Systems

A [`System`](../src/com.radiant.engine/core/System.cs) holds behavior that runs every frame. A
[`Scene`](../src/com.radiant.engine/core/Scene.cs) bundles systems and entities into a level, a menu or any other screen.

## Write a system

Override only the callbacks you need.

The following example shows a system that spins every rectangle and stops while the game is paused:

```csharp
[Pausable]
public class Spinner : System
{
    public float Speed = 2f;

    public override void FixedUpdate()
    {
        float step = Speed * Time.DeltaSeconds;
        Scene.ECS.Query<Transform2D, Rectangle2D>((int thread, int entity, ref Transform2D transform, ref Rectangle2D rectangle) =>
            transform.Rotation += step);
    }
}
```

Register it in your scene with `ECS.AddSystem<Spinner>()`.

## Choose the right callback

| Callback | Use it for |
| --- | --- |
| `Initialize` | Creating render targets, windows and other resources, once |
| `Update` | Key presses, typed text and anything tied to the frame rate |
| `FixedUpdate` | Movement, physics and simulation, 64 times per second |
| `Render` | Producing images other systems read, such as lighting |
| `LateRender` | Overlays and UI drawn on top of the scene |
| `OnResize` | Recreating targets that depend on the window size |
| `Dispose` | Releasing what `Initialize` created |

Every system can use `Time.DeltaSeconds`, `Controls` (input), `Renderer` and `Scene`.

## Order systems

The following example shows how to run after the geometry is built and before the gizmos draw:

```csharp
[RunAfter(typeof(Geometry))]
[RunBefore(typeof(GizmosRenderer))]
public class FogOfWar : System { }
```

Radiant sorts systems once when the scene starts, so registration order never matters. When a scene contains
several instances of one system type, order them by instance with the `After` field.

## Draw on the right layer

`LateRender` output is stacked by `RenderLayer`: `World`, then `Gameplay` (the default), `Overlay` and `UI`.

The following example shows a system whose overlay always draws above game visuals:

```csharp
public class Minimap : System
{
    public override RenderLayer RenderLayer => RenderLayer.Overlay;
}
```

## Pause, tag and protect systems

- `[Pausable]` stops a system while `ECS.GameplayPaused` is set; `[Pausable(PauseGroup.Animation)]` follows
  `ECS.AnimationPaused` instead.
- `[SystemTag("AI", "Gameplay")]` adds categories the System Inspector shows and filters by.
- `[CoreSystem]` hides infrastructure from the System Inspector so nobody switches it off by accident.
- `Enabled = false` skips a system's callbacks until you turn it back on.

## Switch between alternatives

A [`SystemGroup`](../src/com.radiant.engine/core/SystemGroup.cs) runs at most one of its systems at a time and
handles setup and cleanup when you switch.

The following example shows how to offer two upscalers and cycle them with F11:

```csharp
var upscaling = new SystemGroup("Upscaling",
    ("Bilinear", ECS.AddSystem<Bilinear>()),
    ("UDR3", ECS.AddSystem<UDR3>(enabled: false)));
ECS.RegisterSystemGroup(upscaling);

if (Controls.WasPressed(Key.F11)) upscaling.Toggle(); // Bilinear → UDR3 → off → Bilinear
```

## Build a scene

A scene registers systems in `SetupECS` and creates content in `SetupScene`. Its own `Update`, `FixedUpdate`,
`Render` and `LateRender` run just before the systems' callbacks of the same name, which makes them a good place
for scene-wide logic.

The following example shows a scene that spawns a player and toggles pause with P:

```csharp
public class Arena : Scene
{
    public override void SetupECS()
    {
        ECS.AddSystem<Movement2DSystem>();
        ECS.AddSystem<Spinner>();
        base.SetupECS();
    }

    public override void SetupScene()
    {
        int player = ECS.CreateEntity(new Vector2(400, 300));
        ECS.AddComponent<Movement2D>(player);
        base.SetupScene();
    }

    public override void Update()
    {
        if (Controls.WasPressed(Key.P)) ECS.GameplayPaused = !ECS.GameplayPaused;
    }
}
```

Use `ECS.GetSystem<T>()` to reach a registered system, for example `ECS.GetSystem<GizmosRenderer>()`.
