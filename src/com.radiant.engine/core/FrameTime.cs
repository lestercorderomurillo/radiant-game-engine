using System;

namespace com.radiant.engine.core;

/// <summary>Timing for the current callback. Multiply speeds by <c>DeltaSeconds</c> so motion is frame-rate independent.</summary>
/// <param name="Total">Time since the game started.</param>
/// <param name="Elapsed">Time since the previous callback of the same kind.</param>
/// <param name="IsFixedStep">True inside <c>FixedUpdate</c>, where every step has the same length.</param>
public readonly record struct FrameTime(TimeSpan Total, TimeSpan Elapsed, bool IsFixedStep = false)
{
    /// <summary>Seconds to advance by in this callback.</summary>
    public float DeltaSeconds => (float)Elapsed.TotalSeconds;
}
