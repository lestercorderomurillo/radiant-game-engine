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

Declare `[RunAfter(...)]` and `[RunBefore(...)]` on a system, or set `After` between instances, and the engine
sorts the frame for you. `LateRender` output is additionally stacked by `RenderLayer`. See
[Dependency resolution](DependencyResolution.md).

## Pause, group and switch

`[Pausable]`, `Enabled`, `[SystemTag]`, `[CoreSystem]` and `SystemGroup` control which systems run and how the
inspector shows them. See [System control](SystemControl.md).

## Build a scene

A scene registers systems in `SetupECS` and creates content in `SetupScene`; the engine initializes the systems
between the two. See [Scenes](Scenes.md).

Use `ECS.GetSystem<T>()` to reach a registered system, for example `ECS.GetSystem<GizmosRenderer>()`.
