# ECS architecture

Everything in a Radiant scene is an entity: a lightweight ID you attach plain-data components to. Systems then
query the entities that have the components they care about, in parallel.

Reach the scene's [`ECS`](../src/com.radiant.engine/core/ECS/ECS.cs) through `ECS` in a scene, or `Scene.ECS` in a system.

## Create entities

The following example shows how to create entities with and without a position:

```csharp
int marker = ECS.CreateEntity();                              // Just an ID
int player = ECS.CreateEntity(new Vector2(400, 300));         // With a Transform2D
int crate = ECS.CreateEntity(new Vector2(600, 300), layer: 2); // Drawn above layer 0 and 1
int probe = ECS.CreateEntity(new Vector3(0, 1, 0));           // With a Transform3D
```

Entities created with a position are tracked by spatial queries right away.

## Add components

Components are plain structs. `AddComponent` and `GetComponent` return references, so you can assign or edit
them in place.

The following example shows how to give an entity a shape and a glowing material:

```csharp
ECS.AddComponent<Circle2D>(player) = new Circle2D(24f);

ref var material = ref ECS.AddComponent<Material>(player);
material.Emissive = new Color(120, 200, 255);
```

> Adding a component moves the entity in memory. Add all components first, then take the references you need.

Radiant ships with components for the common cases:

| Component | What it does |
| --- | --- |
| `Transform2D`, `Transform3D` | Position, rotation, scale (and draw layer in 2D) |
| `Circle2D`, `Rectangle2D`, `Triangle2D` | 2D shapes drawn by `Geometry` |
| `Material` | Color, light emission and how much light a shape blocks |
| `Movement2D`, `Movement3D` | Velocity and acceleration, applied by the movement systems |
| `Camera2D`, `Camera3D` | Cameras |
| `Mesh3D`, `SurfaceEmission` | 3D meshes and glowing 3D surfaces |

## Make your own component

The following example shows a health component; the description appears in the Component Inspector:

```csharp
[ComponentDescription("Hit points and regeneration per second.")]
public struct Health : Component
{
    public float Current;
    public float RegenPerSecond;
}
```

## Query entities

`Query` runs your code on every entity that has the requested components, in parallel, and `InRadius`, `InBox`,
`Nearest` and `AtExact` find entities by position with no allocation. See [Queries](Queries.md).

## Group entities with tags

The following example shows how to tag enemies, act on all of them and clean up projectiles:

```csharp
ECS.AddTag(enemy, "enemy");

if (ECS.WithTag("enemy") is { } enemies)
    foreach (int each in enemies) ECS.DisableEntity(each);

ECS.DestroyEntitiesWithTag("projectile");
```

## Remove entities

- `ECS.DestroyEntity(id)` removes an entity immediately.
- `ECS.ScheduleDestroy(id)` removes it at the start of the next update, which is safe inside queries.
- `ECS.DisableEntity(id)` hides it from queries until `ECS.EnableEntity(id)`.

## Pause gameplay

`ECS.GameplayPaused` and `ECS.AnimationPaused` stop systems marked `[Pausable]` while rendering and UI keep
running. See [System control](SystemControl.md).

A scene can use up to 64 component types.
