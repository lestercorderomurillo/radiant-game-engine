using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace com.radiant.engine.runtime;

/// <summary>Owns cancellable preparation and advances scene setup on the window thread.</summary>
internal sealed class StartupSession : IProgress<LoadingProgress>, IDisposable
{
    private readonly CancellationTokenSource Cancellation = new();
    private readonly Func<IProgress<LoadingProgress>, CancellationToken, Task> Prepare;
    private readonly IEnumerator<LoadingProgress> Steps;
    private Task Preparation;
    private LoadingProgress Current = new("Initializing engine...", 0);
    private bool Started;
    private bool Disposed;
    private bool AcceptReports = true;
    private readonly object Gate = new();

    public LoadingProgress Progress => Volatile.Read(ref Current);
    public Exception Error { get; private set; }
    public bool Finished { get; private set; }
    public bool CanAdvanceSynchronously => !Finished && Error == null && (Preparation == null || Preparation.IsCompleted);

    public StartupSession(GameLoop Loop, WindowOptions Options)
    {
        Prepare = Options.PrepareApplication;
        Steps = Loop.PrepareStartup();
    }

    public void Report(LoadingProgress Value)
    {
        ArgumentNullException.ThrowIfNull(Value);
        lock (Gate)
        {
            if (!Disposed && AcceptReports) Volatile.Write(ref Current, Value);
        }
    }

    public void Advance()
    {
        if (Disposed || Finished || Error != null) return;
        try
        {
            if (!Started)
            {
                Started = true;
                if (Prepare != null)
                {
                    Report(new LoadingProgress("Preparing application..."));
                    Preparation = Task.Run(() => Prepare(this, Cancellation.Token), Cancellation.Token);
                    // Observe faults even if the window closes before the task completes.
                    _ = Preparation.ContinueWith(Task => { _ = Task.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                    return;
                }
            }
            if (Preparation != null)
            {
                if (!Preparation.IsCompleted) return;
                Preparation.GetAwaiter().GetResult();
                Preparation = null;
            }
            lock (Gate) AcceptReports = false;
            if (Steps.MoveNext()) Volatile.Write(ref Current, Steps.Current);
            else Finished = true;
        }
        catch (Exception Failure)
        {
            Error = Failure;
            lock (Gate) AcceptReports = false;
            Volatile.Write(ref Current, new LoadingProgress("Startup failed. Press Escape to close."));
            core.EngineLog.Write($"Startup failed: {Failure}", core.LogLevel.Error, "Startup");
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (Disposed) return;
            Disposed = true;
        }
        Cancellation.Cancel();
        Steps.Dispose();
        // A callback may still register with its token while unwinding cancellation.
        if (Preparation is { IsCompleted: false })
            _ = Preparation.ContinueWith(_ => Cancellation.Dispose(), TaskScheduler.Default);
        else Cancellation.Dispose();
    }
}
