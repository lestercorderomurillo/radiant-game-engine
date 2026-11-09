using System;
using System.Runtime.InteropServices;

namespace com.radiant.engine.runtime;

/// <summary>MonoGame.Native's GameWindow.Handle is an SDL_Window pointer.</summary>
internal static class NativeWindow
{
    /// <summary>Maximizes the native game window using the available platform API.</summary>
    [DllImport("mgruntime", EntryPoint = "SDL_MaximizeWindow", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Maximize(IntPtr window);
}
