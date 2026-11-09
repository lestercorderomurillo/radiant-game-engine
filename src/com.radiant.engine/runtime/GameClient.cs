using System;
using com.radiant.engine.core;

namespace com.radiant.engine.runtime;

/// <summary>Starts your game: opens the window, runs the loop and cleans up when the player closes it.</summary>
/// <example>
/// The following example shows a complete <c>Program.cs</c>:
/// <code>
/// using com.radiant.engine.runtime;
///
/// new GameClient().Run(new MyScene(), new WindowOptions { Title = "My game", Maximized = true });
/// </code>
/// The following example shows how to run 64 simulation steps without a window, for tests or servers:
/// <code>
/// new GameClient(new HeadlessBackend(64, TimeSpan.FromSeconds(1.0 / 64))).Run(new MySimulation());
/// </code>
/// </example>
public class GameClient
{
    private readonly IRuntimeBackend Backend;
    private readonly com.radiant.engine.input.IInputSource Input;

    /// <summary>Creates a client for the desktop, or for another backend such as <c>HeadlessBackend</c>. Pass an input source to script or replay input.</summary>
    public GameClient(IRuntimeBackend Backend = null, com.radiant.engine.input.IInputSource Input = null)
    {
        this.Backend = Backend ?? RuntimeBackends.CreateDefault();
        this.Input = Input;
    }

    /// <summary>Runs a scene until the window closes, then disposes it. This call blocks.</summary>
    public void Run(Scene Scene, WindowOptions Options = null)
    {
        ArgumentNullException.ThrowIfNull(Scene);

        var GameLoop = new GameLoop { InputSource = Input };
        GameLoop.AddScene(Scene);

        using var host = Backend.CreateHost(GameLoop, Options ?? new WindowOptions());
        try { host.Run(); }
        finally { GameLoop.Dispose(); }
    }
}
