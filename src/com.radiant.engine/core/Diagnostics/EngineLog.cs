using System;
using System.Collections.Generic;

namespace com.radiant.engine.core;

public enum LogLevel { Info, Warning, Error }

public readonly record struct LogEntry(DateTimeOffset Time, LogLevel Level, string Source, string Message);

/// <summary>Thread-safe, bounded engine diagnostics retained for the Inspector debugger.</summary>
public static class EngineLog
{
    public const int Capacity = 2000;
    private static readonly object Gate = new();
    private static readonly Queue<LogEntry> Entries = new();
    private static long Revision;

    public static void Write(string message, LogLevel level = LogLevel.Info, string source = "Engine")
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (Gate)
        {
            foreach (string line in message.Replace("\r", "").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (Entries.Count == Capacity) Entries.Dequeue();
                Entries.Enqueue(new(DateTimeOffset.Now, level, source, line.Length > 8192 ? line[..8192] : line));
            }
            Revision++;
        }
    }

    public static void Clear()
    {
        lock (Gate) { Entries.Clear(); Revision++; }
    }

    /// <summary>Copies the retained entries only when the reader's revision has changed.</summary>
    public static bool TryRead(ref long revision, out LogEntry[] entries)
    {
        lock (Gate)
        {
            if (revision == Revision) { entries = Array.Empty<LogEntry>(); return false; }
            revision = Revision;
            entries = Entries.ToArray();
            return true;
        }
    }
}
