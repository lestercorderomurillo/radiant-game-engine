# Queries

Find the entities you need in two ways: by the components they have, spread across worker threads, or by where
they are, through a spatial index that every positioned entity joins automatically. Both return results without
allocating.

## Query by component

`Query` runs your code on every entity that has all the requested components (one, two or three), spread
across worker threads.

The following example shows how to regenerate health on every entity that has it:

```csharp
float step = Time.DeltaSeconds;
ECS.Query<Health>((int thread, int entity, ref Health health) =>
    health.Current += health.RegenPerSecond * step);
```

> The callback runs on several threads at once. Change the components you receive, and collect anything else in
> per-thread lists indexed by `thread`.

## Query by position

The following example shows the four spatial queries:

```csharp
ReadOnlySpan<int> hit = ECS.InRadius(new Vector3(blastCenter, 0), 120f);                  // Everything in a blast
ReadOnlySpan<int> picked = ECS.InBox(new Vector3(dragStart, 0), new Vector3(dragEnd, 0)); // A selection box
ReadOnlySpan<int> closest = ECS.Nearest(new Vector3(playerPosition, 0), count: 3);        // Three nearest, closest first
int? cell = ECS.AtExact(32, 64, 0);                                                       // Exact grid cell
```

`Nearest` accepts an optional `maxRadius` to stop searching past a distance. `AtExact` matches within 0.01 units.

## Work in 2D

For 2D entities the third coordinate is the draw layer, so query on the layer your entities live on.

The following example shows how to find enemies on layer 1 around the player:

```csharp
ReadOnlySpan<int> nearby = ECS.InRadius(new Vector3(playerPosition, 1), 200f);
foreach (int entity in nearby)
    if (ECS.HasTag(entity, "enemy")) Alert(entity);
```

## Keep positions current

Spatial queries read the position the index knows about. When you move an entity by editing its transform, call
`ECS.SetPosition` so the index follows; the movement systems do this for you.

The following example shows a teleport that keeps lookups correct:

```csharp
ECS.SetPosition(player, new Vector3(spawnPoint, 0)); // Updates Transform and the spatial index together
```

## Use results safely

A returned span is valid until the next call that changes the index (creating, moving or destroying entities).
Copy the IDs you need into a list before acting on them if your action moves entities.

## Continue

- [ECS architecture](ECS.md): create entities and components.
- [Systems](Systems.md): where to run queries every frame.
