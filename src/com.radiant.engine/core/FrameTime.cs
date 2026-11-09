using System;

namespace com.radiant.engine.core;

/// <summary>Radiant-owned timing for the current ECS phase, independent of the platform backend.</summary>
public readonly record struct FrameTime(TimeSpan Total, TimeSpan Elapsed, bool IsFixedStep = false)
{
    /// <summary>Duration of this update or fixed tick in seconds.</summary>
    public float DeltaSeconds => (float)Elapsed.TotalSeconds;
}
