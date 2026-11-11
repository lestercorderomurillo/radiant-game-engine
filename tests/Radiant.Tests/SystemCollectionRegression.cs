using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.input;

internal static class SystemCollectionRegression
{
    private sealed class Viewport : com.radiant.engine.core.System, IViewportCamera2D
    {
        public string CameraName => "Test tile viewport";
        public Vector2 CameraPosition { get; set; }
    }

    public static void Run(IRenderer renderer)
    {
        using var scene = new Scene { Renderer = renderer };
        scene.Initialize();
        var inspector = scene.ECS.AddSystem<Inspector>();
        scene.ECS.AddSystemCollection(SystemCollections.Debugging);
        scene.ECS.AddSystemCollection(SystemCollections.Debugging);
        Require(scene.ECS.GetAllSystems().Count == 3 && ReferenceEquals(inspector, scene.ECS.GetSystem<Inspector>()),
            "Debug collection duplicated existing systems.");
        int applied = 0;
        var custom = new SystemCollection("Custom", ecs => { applied++; ecs.AddSystem<Viewport>(); });
        scene.ECS.AddSystemCollection(custom);
        scene.ECS.AddSystemCollection(custom);
        Require(applied == 1, "A collection was applied more than once.");
        scene.ECS.Initialize();
        Inspector.CreateWindow("late_tool", "Late tool", Visible: false);
        int entityCamera = scene.ECS.CreateEntity();
        scene.ECS.AddComponent<Camera2D>(entityCamera);
        inspector.Update();
        Require(inspector.Visible && inspector.CurrentWorkspaceMode == WorkspaceMode.Panels
            && renderer.ScenePresentationBounds is { Width: > 0, Height: > 0 }, "Visible panels are not the default.");
        foreach (string id in new[] { "inspector", "perf", "debugger", "entity_inspector", "system_inspector", "component_inspector", "camera_controls", "late_tool" })
            Require(Inspector.IsWindowVisible(id), $"Show All missed {id}.");
        Inspector.HideWindow("late_tool");
        inspector.Update();
        Require(!Inspector.IsWindowVisible("late_tool"), "Startup Show All reopened a tool on subsequent frames.");
        try { scene.ECS.AddSystemCollection(custom); throw new Exception("Late collection registration was accepted."); }
        catch (InvalidOperationException) { }

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(string name) => typeof(Inspector).GetField(name, flags).GetValue(inspector);
        var windows = (Dictionary<string, WindowData>)Field("Windows");
        var componentBrowser = windows["component_inspector"];
        Require(componentBrowser.Widgets[componentBrowser.WidgetIndex["components_list"]].ListBoxItems.Any(row => row.Contains("Camera2D")),
            "Component browser retained its empty initialization list.");
        var camera = windows["camera_controls"];
        var choice = camera.Widgets[camera.WidgetIndex["camera"]];
        int tile = Array.IndexOf(choice.DropdownOptions, "Test tile viewport");
        Require(tile >= 0, "Renderer-owned tile camera was not discovered.");
        choice.DropdownCallback(tile);
        camera.Widgets[camera.WidgetIndex["navigateCamera"]].ToggleCallback(true);
        var game = renderer.ScenePresentationBounds.Value;
        scene.DeltaTime = 0.1f;
        scene.Controls = new InputFrame(new(new KeyboardState(Key.D), new MouseState(game.Center.X, game.Center.Y,
            RightButton: ButtonState.Pressed), IsFocused: true), default);
        typeof(Inspector).GetMethod("UpdateCameraNavigation", flags).Invoke(inspector, null);
        Require(scene.ECS.GetSystem<Viewport>().CameraPosition.X > 0 && Inspector.IsCameraInputActive,
            "Tile viewport navigation did not move or claim gameplay input.");
        scene.ECS.DestroyEntity(entityCamera);
        inspector.Update();
        var cameraChoices = camera.Widgets[camera.WidgetIndex["camera"]].DropdownOptions;
        Require(!cameraChoices.Any(label => label.StartsWith("Entity ")), "Camera discovery retained a deleted entity.");
        inspector.Visible = false;
        Require(renderer.ScenePresentationBounds == null && !Inspector.IsCameraInputActive, "Hiding tools retained their viewport or mouse capture.");

        using var other = new Scene { Renderer = renderer };
        other.Initialize();
        other.ECS.AddSystemCollection(SystemCollections.Debugging);
        Require(!ReferenceEquals(inspector, other.ECS.GetSystem<Inspector>())
            && !ReferenceEquals(scene.ECS.GetSystem<PerformanceMonitor>(), other.ECS.GetSystem<PerformanceMonitor>()),
            "Debug systems are shared between scenes.");
        Console.WriteLine("PASS: reusable collections, per-scene instances, default Panels / Show All, late windows and tile camera navigation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
