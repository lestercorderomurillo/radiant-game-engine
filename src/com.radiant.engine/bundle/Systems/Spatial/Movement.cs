using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

/// <summary>Opt-in semi-implicit Euler movement; synchronizes spatial lookups after parallel integration.</summary>
[Pausable]
public class Movement2DSystem : core.System
{
    private List<(int Entity, Vector2 Position)>[] Moved;

    /// <inheritdoc/>
    public override void Initialize()
    {
        Moved = new List<(int, Vector2)>[ECS.ThreadCount];

        for (int i = 0; i < Moved.Length; i++) Moved[i] = new();
    }

    /// <inheritdoc/>
    public override void FixedUpdate()
    {
        foreach (var worker in Moved) worker.Clear();

        float dt = Scene.DeltaTime;
        Scene.ECS.Query((int thread, int entity, ref Transform2D transform, ref Movement2D movement) =>
        {
            movement.Speed += movement.Acceleration * dt;
            transform.Position += movement.Speed * dt;
            Moved[thread].Add((entity, transform.Position));
        });

        // SpatialIndex is single-threaded; update it only after all query workers finish.
        foreach (var worker in Moved) foreach (var move in worker) Scene.ECS.SetPosition(move.Entity, move.Position);
    }
}

/// <summary>Opt-in semi-implicit Euler movement; synchronizes spatial lookups after parallel integration.</summary>
[Pausable]
public class Movement3DSystem : core.System
{
    private List<(int Entity, Vector3 Position)>[] Moved;

    /// <inheritdoc/>
    public override void Initialize()
    {
        Moved = new List<(int, Vector3)>[ECS.ThreadCount];

        for (int i = 0; i < Moved.Length; i++) Moved[i] = new();
    }

    /// <inheritdoc/>
    public override void FixedUpdate()
    {
        foreach (var worker in Moved) worker.Clear();

        float dt = Scene.DeltaTime;
        Scene.ECS.Query((int thread, int entity, ref Transform3D transform, ref Movement3D movement) =>
        {
            movement.Speed += movement.Acceleration * dt;
            transform.Position += movement.Speed * dt;
            Moved[thread].Add((entity, transform.Position));
        });

        // SpatialIndex is single-threaded; update it only after all query workers finish.
        foreach (var worker in Moved) foreach (var move in worker) Scene.ECS.SetPosition(move.Entity, move.Position);
    }
}

