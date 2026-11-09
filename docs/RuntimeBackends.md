# Running your game

`GameClient` opens the window, runs the game loop and cleans up when the player quits. The same scenes also run
without a window for tests, tools and servers.

## Open a window

The following example shows a complete `Program.cs`:

```csharp
using com.radiant.engine.runtime;

new GameClient().Run(new MyScene(), new WindowOptions
{
    Title = "My game",
    Width = 1280,
    Height = 720,
    Maximized = false
});
```

The world keeps the same coordinates whatever size the window has, so resizing never moves your content.

## Run without a window

`HeadlessBackend` runs a fixed number of updates with an exact time step and no graphics device. It's perfect
for deterministic tests and simulations.

The following example shows how to simulate one second at 64 updates per second:

```csharp
new GameClient(new HeadlessBackend(64, TimeSpan.FromSeconds(1.0 / 64)))
    .Run(new MySimulationScene());
```

In headless runs `Renderer` is null and render callbacks never run, so keep simulation logic in systems that
don't draw.

## Use time correctly

Every scene and system has `Time`:

| Property | Meaning |
| --- | --- |
| `Time.DeltaSeconds` | Seconds to advance in this callback |
| `Time.Total` | Time since the game started |
| `Time.IsFixedStep` | True inside `FixedUpdate` |

`FixedUpdate` always receives exactly 1/64 of a second, which keeps physics stable. `Update` receives the real
frame time. After a long stall the loop catches up at most eight fixed steps per frame, so the game slows down
briefly instead of freezing.

The following example shows frame-rate independent movement:

```csharp
public override void FixedUpdate()
{
    position += velocity * Time.DeltaSeconds;
}
```

## Control the frame rate

Rendering targets 144 frames per second by default. The following example shows how to cap it at 60, or pass 0
for no limit:

```csharp
Renderer.GameLoop.SetTargetFps(60);
```

Lit 2D scenes also offer an **FPS Limit** dropdown in their pipeline window.

## Write your own host

To run Radiant on another platform, implement `IRuntimeBackend.CreateHost(gameLoop, options)` and return an
`IRuntimeHost`. Your host:

1. Initializes the platform and calls `gameLoop.Initialize(host)` once.
2. Calls `gameLoop.Update()` every tick, and `gameLoop.Render()` while a frame is active on the same thread.
3. Provides a renderer in `PrepareScene`, presents in `BeginFrame` / `EndFrame`, and releases scene resources in
   `ReleaseScene`, which must tolerate a scene that only partly started.

`GameClient` creates a fresh loop and host for each run and disposes both, even if the game throws.
