using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using com.radiant.engine.core;
using com.radiant.engine.runtime;

internal static class RendererContractRegression
{
    public static void Run()
    {
        _ = new GameClient(); // The desktop distribution remains discoverable without creating a window.
        var contracts = new[] { typeof(IRenderer), typeof(IWindow), typeof(IComputeShader) };
        bool Native(Type type) => type == typeof(Renderer) || type == typeof(Window) ||
            (type.Namespace?.StartsWith("Microsoft.Xna") ?? false) ||
            (type.Namespace?.StartsWith("FontStashSharp") ?? false) ||
            (type.HasElementType && Native(type.GetElementType())) ||
            (type.IsGenericType && type.GetGenericArguments().Any(Native));
        foreach (var contract in contracts.SelectMany(t => t.GetInterfaces().Append(t)))
        {
            foreach (var method in contract.GetMethods())
            {
                Require(!Native(method.ReturnType), "Nonportable result: " + method);
                foreach (var parameter in method.GetParameters()) Require(!Native(parameter.ParameterType), "Nonportable input: " + method);
            }
        }
        Require(typeof(Scene).GetProperty("Renderer").PropertyType == typeof(IRenderer), "Scene renderer is concrete");
        Require(typeof(ECS).GetProperty("Renderer").PropertyType == typeof(IRenderer), "ECS renderer is concrete");
        Require(typeof(com.radiant.engine.core.System).GetField("Renderer").FieldType == typeof(IRenderer), "System renderer is concrete");
        foreach (string name in new[] { "SupportsComputeShaders", "CreateComputeShader", "SupportsVulkanPathTracing", "SupportsNrdDenoising" })
            Require(!(typeof(IRenderer).GetMethod(name) ?? typeof(IComputeRenderer).GetMethod(name)).IsAbstract, "Optional capability requires implementation: " + name);

        IComputeRenderer unsupported = new NoComputeRenderer();
        Require(!unsupported.SupportsComputeShaders(out var reason) && !string.IsNullOrWhiteSpace(reason), "Default compute capability");
        try { unsupported.CreateComputeShader([], 1, 1); throw new Exception("Unsupported compute allocation accepted"); }
        catch (NotSupportedException) { }

        var renderer = DispatchProxy.Create<IRenderer, RecordingRenderer>();
        var recording = (RecordingRenderer)renderer;
        var scene = new DrawingScene();
        new GameClient(new RecordingBackend(renderer)).Run(scene);
        Require(ReferenceEquals(scene.Probe.Renderer, renderer), "Host renderer was replaced before ECS injection");
        Require(string.Join(",", recording.Calls) == "clear-frame,reset,clear,commit,close,present,dispose", "Command/lifetime order: " + string.Join(",", recording.Calls));
        Require(recording.Window.Closed, "Portable close request not delivered");
        Console.WriteLine("PASS: portable renderer/window/compute contracts, interchangeable drawing backend, fluent ECS commands and host cleanup without a graphics device.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    // A recording implementation supplied by the consumer; no Renderer/Window construction.
    public class RecordingRenderer : DispatchProxy
    {
        public readonly List<string> Calls = new();
        public readonly RecordingWindow Window;
        public RecordingRenderer() => Window = new RecordingWindow(Calls);
        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_Window": return Window;
                case "get_HasPendingResize": return false;
                case "ClearBackBuffer": Calls.Add("clear-frame"); return null;
                case "Reset": Calls.Add("reset"); return this;
                case "Clear": Require((Color)args[0] == Color.Coral, "Draw color lost"); Calls.Add("clear"); return this;
                case "Commit": Calls.Add("commit"); return this;
                case "PresentToBackBuffer": Calls.Add("present"); return null;
                case "Dispose": Calls.Add("dispose"); return null;
                default: throw new NotSupportedException(method.Name);
            }
        }
    }
    public sealed class RecordingWindow(List<string> calls) : IWindow
    {
        public bool IsActive => true;
        public string Title { get; set; } = "Recording";
        public Rectangle ClientBounds => new(0, 0, 320, 240);
        public bool Closed;
        public void RequestClose() { Closed = true; calls.Add("close"); }
    }
    private sealed class NoComputeRenderer : IComputeRenderer { }

    private sealed class DrawingScene : Scene
    {
        internal DrawingSystem Probe;
        public override void SetupECS() { Probe = ECS.AddSystem<DrawingSystem>(); base.SetupECS(); }
    }
    private sealed class DrawingSystem : com.radiant.engine.core.System
    {
        public override void Render()
        {
            Renderer.Reset().Clear(Color.Coral).Commit();
            Renderer.Window.RequestClose();
        }
    }
    private sealed class RecordingBackend(IRenderer renderer) : IRuntimeBackend
    {
        public IRuntimeHost CreateHost(GameLoop loop, WindowOptions options) => new Host(loop, renderer);
        private sealed class Host(GameLoop loop, IRenderer renderer) : IRuntimeHost
        {
            public bool IsActive => true;
            public void Run() { loop.Initialize(this); loop.SetTargetFps(0); loop.Update(TimeSpan.FromSeconds(1.0 / 64)); loop.Render(); }
            public void PrepareScene(Scene scene) => scene.Renderer = renderer;
            public void BeginFrame(Scene scene) => scene.Renderer.ClearBackBuffer(Color.Black);
            public void EndFrame(Scene scene) => scene.Renderer.PresentToBackBuffer();
            public void ReleaseScene(Scene scene) { scene.Renderer?.Dispose(); scene.Renderer = null; }
            public void Dispose() { }
        }
    }
}
