using System;
using com.radiant.engine.core;

namespace com.radiant.engine.runtime;

/// <summary>Creates an independent platform session for a Radiant application.</summary>
public interface IRuntimeBackend
{
    /// <summary>Creates a host; Run must initialize the loop before invoking its update/render phases.</summary>
    IRuntimeHost CreateHost(GameLoop Loop, WindowOptions Options);
}

/// <summary>
/// Platform lifecycle boundary. The host owns presentation resources; GameLoop owns scenes
/// and ECS scheduling. Invoke loop callbacks on one thread. Dispose the loop before the host.
/// This is a runtime boundary, not yet a portable graphics resource API.
/// </summary>
public interface IRuntimeHost : IDisposable
{
    bool IsActive { get; }
    com.radiant.engine.input.IInputSource Input => com.radiant.engine.input.NullInputSource.Instance;
    void Run();
    /// <summary>Creates scene presentation resources before scene/system initialization.</summary>
    void PrepareScene(Scene Scene);
    /// <summary>Releases presentation resources after scene/system disposal. Must tolerate partial preparation.</summary>
    void ReleaseScene(Scene Scene);
    void BeginFrame(Scene Scene);
    void EndFrame(Scene Scene);
}
