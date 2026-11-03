using System;
using System.Runtime.InteropServices;

namespace com.radiant.engine.runtime;

// MonoGame.Native's GameWindow.Handle is an SDL_Window pointer.
internal static class NativeWindow
{
    [DllImport("mgruntime", EntryPoint = "SDL_MaximizeWindow", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Maximize(IntPtr window);
}
