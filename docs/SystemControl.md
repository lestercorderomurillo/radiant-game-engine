# System control

Pause gameplay without stopping the UI, hide infrastructure from the inspector, and switch between alternative
systems at runtime.

## Pause gameplay or animation

Two independent pause flags let menus and cutscenes stop exactly what they should.

The following example shows how to pause movement and AI while rendering and UI keep running:

```csharp
ECS.GameplayPaused = true;   // Stops systems marked [Pausable]
ECS.AnimationPaused = true;  // Stops systems marked [Pausable(PauseGroup.Animation)]
```

Mark the systems that should obey each flag:

```csharp
[Pausable]                            // Follows ECS.GameplayPaused
public class EnemyAI : System { }

[Pausable(PauseGroup.Animation)]      // Follows ECS.AnimationPaused
public class SpriteAnimator : System { }
```

Systems without the attribute always run.

## Enable and disable

`Enabled = false` skips a system's callbacks until you turn it back on.

The following example shows how to toggle a debug overlay with a key:

```csharp
var gizmos = ECS.GetSystem<GizmosRenderer>();
if (Controls.WasPressed(Key.G)) gizmos.Enabled = !gizmos.Enabled;
```

## Tag and protect

- `[SystemTag("AI", "Gameplay")]` adds categories the System Inspector shows and filters by.
- `[CoreSystem]` hides infrastructure from the System Inspector so nobody switches it off by accident.

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

`ActiveName` and `Active` report the current choice, `DisableActive()` turns everything in the group off, and
`ForEach` visits every member. The group name appears next to its systems in the System Inspector.

## Continue

- [Dependency resolution](DependencyResolution.md): how switched systems are ordered.
- [Inspector](Inspector.md): the System Inspector window.
