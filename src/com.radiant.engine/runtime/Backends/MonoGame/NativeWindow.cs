using System;
using System.Runtime.InteropServices;

namespace com.radiant.engine.runtime;

/// <summary>MonoGame.Native's GameWindow.Handle is an SDL_Window pointer.</summary>
internal static class NativeWindow
{
    /// <summary>Sizes the SDL surface before Vulkan creates its replacement swapchain.</summary>
    [DllImport("mgruntime", EntryPoint = "SDL_SetWindowSize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void SetSize(IntPtr window, int width, int height);

    /// <summary>Maximizes the native game window using the available platform API.</summary>
    [DllImport("mgruntime", EntryPoint = "SDL_MaximizeWindow", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Maximize(IntPtr window);
}
