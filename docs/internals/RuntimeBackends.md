# Radiant runtime backends

Applications start through Radiant's `GameClient`, `Scene` and ECS APIs. The runtime
loop now depends on `IRuntimeHost`, not on `MonoGame.Game`, `Window`, `GameTime`,
`SpriteBatch` or `GraphicsDevice`. This migration separates application lifecycle, simulation and input; it is not yet
a graphics-backend abstraction.

## Application entry point

```csharp
using com.radiant.engine.runtime;

new GameClient().Run(new MyScene(), new WindowOptions
{
    Title = "My game", Width = 1280, Height = 720
});
```

The default backend is `MonoGameBackend`. Game code does not need to subclass
`Window` or `Game`. Existing desktop regression fixtures can still subclass `Window`;
that class is the MonoGame-specific host and is not a portable application API.

## Simulation without a window

```csharp
using System;
using com.radiant.engine.runtime;

new GameClient(new HeadlessBackend(64, TimeSpan.FromSeconds(1.0 / 64)))
    .Run(new MySimulationScene());
```

This executes 64 updates with deterministic elapsed time, runs fixed ticks through
the same ECS scheduler, then disposes the scene. It creates no graphics device and
never invokes render callbacks. Use simulation-only systems: a system that requires
textures, desktop input or a renderer is not headless-compatible. `Scene.Renderer`
and `System.Renderer` are null in this host. The assembly still references MonoGame;
this is not yet a standalone dependency-free core package.

## Radiant time

Scenes and systems expose `FrameTime Time`:

- `Time.Total`: accumulated variable time, or accumulated simulation tick time in FixedUpdate.
- `Time.Elapsed`: duration of the current phase.
- `Time.DeltaSeconds`: that duration in seconds.
- `Time.IsFixedStep`: true only during the fixed simulation callback.

The loop owns these clocks. It no longer forwards MonoGame's timing object. Fixed
updates receive exactly 1/64 second. Variable updates receive their actual duration;
rendering receives the latest variable-update time. Catch-up remains capped at eight
fixed ticks per update, so fixed simulation time can lag wall time after a stall.

Migration: replace `GameTime.ElapsedGameTime.TotalSeconds` with `Time.DeltaSeconds`
and `GameTime.TotalGameTime` with `Time.Total`. `Scene.DeltaTime` remains available
for existing systems. The unused `GameLoop.SpriteBatch` and extra window SpriteBatch
were removed; draw through the renderer.

## Implementing another runtime host

Implement `IRuntimeBackend.CreateHost(GameLoop, WindowOptions)` and return an
`IRuntimeHost`. `GameClient` owns the returned host and disposes the loop before
releasing the host, including when Run throws. Each run gets a new loop and host.

The host initializes its platform resources and calls `Loop.Initialize(this)` once.
It then calls `Loop.Update()` with the built-in stopwatch, or `Loop.Update(elapsed)`
with explicit durations. Choose one clock source consistently. Graphical hosts also
call `Loop.Render()` while a graphics frame is active, on the same thread.

The loop calls the host in this order:

1. `PrepareScene`: create presentation resources before scene/system initialization.
2. Scene initialization, ECS setup, entity setup and simulation callbacks.
3. For each rendered frame: `BeginFrame`, scene/ECS render phases, `EndFrame`.
4. Scene/system disposal, then `ReleaseScene`, before disposing platform resources.

`ReleaseScene` must tolerate partial preparation. `Dispose` on the loop is idempotent.
Scene transitions release the previous scene before preparing the next one. The host
must stay alive through scene disposal so systems can release their GPU resources.

The MonoGame adapter constructs the existing Renderer and presents its output.
The headless adapter has no presentation resources. Tests also use a separate host
implementation to verify transitions and exception cleanup through this contract.

## Remaining graphics migration

The runtime interface intentionally does not promise arbitrary graphics backends yet.
`Renderer` still implements drawing through MonoGame and retains obsolete native
device/SpriteBatch accessors; `Window` remains the native desktop host.
Keyboard, mouse and text input use [Radiant input contracts](Input.md); vectors,
matrices, colors, integer geometry and mesh vertices use [backend-independent math](Math.md).
Blend, sampler, rasterizer and depth/stencil settings use [Radiant render states](RenderStates.md).
Textures, render targets and effects use [Radiant resource contracts](GraphicsResources.md).
Scenes and systems now consume [IRenderer and IWindow](RendererContracts.md).
The MonoGame renderer implements these portable contracts; compute and native tracing
are optional capabilities. A separate GPU backend is not implemented yet.

Future command interfaces should use Radiant resource contracts, never native
Texture2D, Effect or GraphicsDevice parameters. Do not assume a
backend implements Vulkan, compute or hardware tracing simply because it implements
the runtime host interface.

## Verification

`RuntimeBackendRegression` uses only Radiant and .NET types and verifies headless
simulation, timing propagation, a second host implementation, scene transitions,
fixed-tick catch-up limits and cleanup after failure. Run the full CPU suite with:

```sh
dotnet run --project tests/Radiant.Tests -p:RadiantBuildContent=false
```

The desktop Vulkan RT and compute regressions also cover compatibility with the
existing graphical host. No new graphics backend or MonoGame fork is required for
this runtime separation.

## Check sibling games after API changes

With Pacman and Zuma cloned beside the engine, run:

```sh
python eng/check-consumers.py
```

This builds all eight projects across the engine and both game solutions, including
content, then runs the three deterministic suites. `--configuration Debug` checks
Debug instead of Release; `--pacman-root` and `--zuma-root` accept custom locations.
Builds run sequentially because the solutions share engine outputs. Graphical checks
remain separate and must use isolated displays as documented in the games' BUILD.md.
