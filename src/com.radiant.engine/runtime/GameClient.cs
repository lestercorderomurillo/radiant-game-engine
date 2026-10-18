using System;
using com.radiant.engine.core;

namespace com.radiant.engine.runtime;

/// <summary>Hosts a game-owned scene using Radiant's window, renderer and game loop.</summary>
public class GameClient
{
    /// <summary>Runs the supplied scene until the window closes, then releases its resources.</summary>
    public void Run(Scene Scene, WindowOptions Options = null)
    {
        ArgumentNullException.ThrowIfNull(Scene);
        var GameLoop = new GameLoop();
        GameLoop.AddScene(Scene);
        using var Window = new Window(GameLoop, Options);
        Window.Run();
    }
}
