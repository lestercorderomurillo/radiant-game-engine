using System;
using com.radiant.engine.core;

namespace com.radiant.engine.runtime;

/// <summary>Hosts a game-owned scene using Radiant's window, renderer and game loop.</summary>
public class GameClient
{
    private readonly IRuntimeBackend Backend;
    private readonly com.radiant.engine.input.IInputSource Input;

    /// <summary>Uses the default desktop backend, or an explicitly selected runtime backend.</summary>
    public GameClient(IRuntimeBackend Backend = null, com.radiant.engine.input.IInputSource Input = null)
    {
        this.Backend = Backend ?? new MonoGameBackend();
        this.Input = Input;
    }

    /// <summary>Runs the supplied scene until the window closes, then releases its resources.</summary>
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
