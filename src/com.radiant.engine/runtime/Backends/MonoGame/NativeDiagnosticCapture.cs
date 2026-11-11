using System;
using System.IO;
using System.Runtime.InteropServices;
using com.radiant.engine.core;

namespace com.radiant.engine.backends.monogame;

/// <summary>Captures C-runtime output during graphics initialization, including driver diagnostics.
/// Managed Console redirection alone cannot capture Vulkan's printf/fprintf calls.</summary>
internal sealed class NativeDiagnosticCapture : IDisposable
{
    private static readonly object Gate = new();
    private static NativeDiagnosticCapture Active;
    private readonly string Path;
    private int SavedOut = -1, SavedError = -1;
    private bool Disposed;

    private NativeDiagnosticCapture()
    {
        Path = System.IO.Path.GetTempFileName();
        int target = Open(Path);
        if (target < 0) { File.Delete(Path); throw new IOException("Cannot open native diagnostic capture."); }
        try
        {
            Flush();
            SavedOut = Dup(1);
            SavedError = Dup(2);
            if (SavedOut < 0 || SavedError < 0 || Dup2(target, 1) < 0 || Dup2(target, 2) < 0)
                throw new IOException("Cannot redirect native diagnostics.");
        }
        catch { Dispose(); throw; }
        finally { Close(target); }
    }

    public static NativeDiagnosticCapture Begin()
    {
        lock (Gate)
        {
            if (Active != null) return null;
            try { return Active = new NativeDiagnosticCapture(); }
            catch (Exception error)
            {
                EngineLog.Write(error.Message, LogLevel.Warning, "Diagnostics");
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (Disposed) return;
            Disposed = true;
            Flush();
            if (SavedOut >= 0) { Dup2(SavedOut, 1); Close(SavedOut); }
            if (SavedError >= 0) { Dup2(SavedError, 2); Close(SavedError); }
            Active = null;
            try
            {
                foreach (string line in File.ReadLines(Path))
                {
                    var level = line.Contains("fail", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        ? LogLevel.Error : line.Contains("warning", StringComparison.OrdinalIgnoreCase) ? LogLevel.Warning : LogLevel.Info;
                    EngineLog.Write(line, level, "Graphics");
                }
            }
            catch (IOException error) { EngineLog.Write(error.Message, LogLevel.Warning, "Diagnostics"); }
            finally
            {
                try { File.Delete(Path); }
                catch (IOException error) { EngineLog.Write(error.Message, LogLevel.Warning, "Diagnostics"); }
                catch (UnauthorizedAccessException error) { EngineLog.Write(error.Message, LogLevel.Warning, "Diagnostics"); }
            }
        }
    }

    private static int Open(string path) => OperatingSystem.IsWindows() ? WindowsOpen(path, 0x8002) : UnixOpen(path, 2);
    private static int Dup(int fd) => OperatingSystem.IsWindows() ? WindowsDup(fd) : UnixDup(fd);
    private static int Dup2(int source, int target) => OperatingSystem.IsWindows() ? WindowsDup2(source, target) : UnixDup2(source, target);
    private static void Close(int fd) { if (OperatingSystem.IsWindows()) WindowsClose(fd); else UnixClose(fd); }
    private static void Flush() { if (OperatingSystem.IsWindows()) WindowsFlush(IntPtr.Zero); else UnixFlush(IntPtr.Zero); }

    [DllImport("libc", EntryPoint = "open")] private static extern int UnixOpen(string path, int flags);
    [DllImport("libc", EntryPoint = "dup")] private static extern int UnixDup(int fd);
    [DllImport("libc", EntryPoint = "dup2")] private static extern int UnixDup2(int source, int target);
    [DllImport("libc", EntryPoint = "close")] private static extern int UnixClose(int fd);
    [DllImport("libc", EntryPoint = "fflush")] private static extern int UnixFlush(IntPtr stream);
    [DllImport("ucrtbase", EntryPoint = "_wopen", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)] private static extern int WindowsOpen(string path, int flags);
    [DllImport("ucrtbase", EntryPoint = "_dup", CallingConvention = CallingConvention.Cdecl)] private static extern int WindowsDup(int fd);
    [DllImport("ucrtbase", EntryPoint = "_dup2", CallingConvention = CallingConvention.Cdecl)] private static extern int WindowsDup2(int source, int target);
    [DllImport("ucrtbase", EntryPoint = "_close", CallingConvention = CallingConvention.Cdecl)] private static extern int WindowsClose(int fd);
    [DllImport("ucrtbase", EntryPoint = "fflush", CallingConvention = CallingConvention.Cdecl)] private static extern int WindowsFlush(IntPtr stream);
}
