using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using com.radiant.engine.core;
using com.radiant.engine.backends.monogame;

internal static class LoggingRegression
{
    public static void Run()
    {
        EngineLog.Clear();
        Parallel.For(0, 3000, i => EngineLog.Write($"message {i}"));
        long revision = -1;
        if (!EngineLog.TryRead(ref revision, out var entries) || entries.Length != EngineLog.Capacity)
            throw new Exception("Concurrent log writes did not respect capacity.");
        if (EngineLog.TryRead(ref revision, out _)) throw new Exception("Unchanged logs were copied again.");
        EngineLog.Clear();
        if (!EngineLog.TryRead(ref revision, out entries) || entries.Length != 0)
            throw new Exception("Clearing logs did not notify readers.");

        try
        {
            using var capture = NativeDiagnosticCapture.Begin();
            if (capture == null) throw new Exception("Native capture could not start.");
            if (OperatingSystem.IsWindows()) WindowsPuts("WARNING: native capture regression");
            else UnixPuts("WARNING: native capture regression");
            var error = System.Text.Encoding.UTF8.GetBytes("ERROR: native stderr regression\n");
            if (OperatingSystem.IsWindows()) WindowsWrite(2, error, error.Length);
            else UnixWrite(2, error, (nuint)error.Length);
            throw new InvalidOperationException("Simulated initialization failure");
        }
        catch (InvalidOperationException) { }
        if (!EngineLog.TryRead(ref revision, out entries) || !entries.Any(entry =>
            entry.Message == "WARNING: native capture regression" && entry.Level == LogLevel.Warning))
            throw new Exception("Native output was not retained on initialization failure.");
        if (!entries.Any(entry => entry.Message == "ERROR: native stderr regression" && entry.Level == LogLevel.Error))
            throw new Exception("Native stderr diagnostics were lost.");
        // A second scope proves the first restored its descriptors and released ownership.
        using (var capture = NativeDiagnosticCapture.Begin())
            if (capture == null) throw new Exception("Native capture remained active after disposal.");
        EngineLog.Clear();
        Console.WriteLine("PASS: bounded concurrent logging, clear, and native capture on failure.");
    }

    [DllImport("libc", EntryPoint = "puts")] private static extern int UnixPuts(string message);
    [DllImport("libc", EntryPoint = "write")] private static extern nint UnixWrite(int fd, byte[] bytes, nuint count);
    [DllImport("ucrtbase", EntryPoint = "puts", CallingConvention = CallingConvention.Cdecl)] private static extern int WindowsPuts(string message);
    [DllImport("ucrtbase", EntryPoint = "_write", CallingConvention = CallingConvention.Cdecl)] private static extern int WindowsWrite(int fd, byte[] bytes, int count);
}
