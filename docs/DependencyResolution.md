# Dependency resolution

Declare what each system must run before or after, and the engine orders the whole frame for you. Registration
order never matters, and adding a system never means editing a scheduling list.

## Declare dependencies

The following example shows a system that runs after the geometry is built and before the gizmos draw:

```csharp
[RunAfter(typeof(Geometry))]
[RunBefore(typeof(GizmosRenderer))]
public class FogOfWar : System { }
```

Both attributes accept several types and can be repeated. Radiant sorts the systems once when the scene starts
with a topological sort, so cycles are reported as an error at startup rather than hidden at runtime.

## Order instances of one type

When a scene contains several instances of the same system type, for example two `ToneMapping` stages, order
them by instance with the `After` field instead of attributes.

The following example shows an image chain where each stage waits for the previous one:

```csharp
var denoiser = ECS.AddSystem(new Denoising { Input = tracer.GetOutput, After = [tracer] });
var color = ECS.AddSystem(new ToneMapping { Input = denoiser.GetOutput, After = [denoiser] });
var aa = ECS.AddSystem(new AntiAliasing { Input = color.GetOutput, After = [color] });
```

## Separate render order from draw order

`Render` callbacks follow the dependency order exactly, because later systems read what earlier ones produced.
`LateRender` is stacked by `RenderLayer` first (`World`, `Gameplay`, `Overlay`, `UI`) and only then by
dependency, so a minimap always lands above the game even if it was registered first.

The following example shows a system whose overlay always draws above game visuals:

```csharp
public class Minimap : System
{
    public override RenderLayer RenderLayer => RenderLayer.Overlay;
}
```

## Disabled systems

A system registered with `enabled: false` is left out of the sort until it is activated, which is how a
[`SystemGroup`](SystemControl.md) swaps alternatives without rebuilding the scene.

## Continue

- [Systems](Systems.md): write systems and choose their callbacks.
- [System control](SystemControl.md): pause, group and switch systems at runtime.
