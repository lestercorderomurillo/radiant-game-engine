using System;

namespace com.radiant.engine.runtime;

/// <summary>A real startup or update milestone. Null completion means the amount of work is unknown.</summary>
public sealed record LoadingProgress
{
    public string Message { get; }
    public double? Completion { get; }

    /// <summary>Creates a single-line status with an optional finite completion fraction from zero to one.</summary>
    public LoadingProgress(string Message, double? Completion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);
        if (Message.Contains('\n') || Message.Contains('\r'))
            throw new ArgumentException("Loading status must be a single line.", nameof(Message));
        if (Completion.HasValue && (!double.IsFinite(Completion.Value) || Completion < 0 || Completion > 1))
            throw new ArgumentOutOfRangeException(nameof(Completion));
        this.Message = Message;
        this.Completion = Completion;
    }
}
