using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using com.radiant.engine.input;
using Microsoft.Xna.Framework;

/// <summary>Runs editor regression checks and records diagnostic frames.</summary>
internal sealed class EditorRegressionWindow : Window
{
    private readonly Scene Scene;
    private readonly bool TwoD;
    private readonly bool Navigation;
    private int Frame, CameraEntity;
    private readonly ScriptedInput Input = new();
    private sealed class ScriptedInput : IInputSource
    {
        public InputSnapshot State;
        public InputSnapshot Capture()
        {
            var current = State;
            State = State with { Text = "" };
            return current;
        }
    }
    private Dictionary<string, WindowData> EditorWindows => (Dictionary<string, WindowData>)typeof(Inspector).GetField("Windows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Scene.ECS.GetSystem<Inspector>());

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public EditorRegressionWindow(bool twoD, bool navigation = false) : base(new GameLoop(), new() { Title = "Radiant editor regression", Width = 1280, Height = 720 })
    {
        TwoD = twoD;
        Navigation = navigation;
        Scene = twoD ? new Rendering2DScene() : new CornellBoxScene();
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
        if (Navigation) { GameLoop.SetTargetFps(60); GameLoop.InputSource = Input; }
    }

    /// <summary>Finds a named inspector widget required by the regression case.</summary>
    private Widget Widget(string window, string id) => EditorWindows[window].Widgets[EditorWindows[window].WidgetIndex[id]];

    /// <summary>Selects the component under test in the property inspector.</summary>
    private void SelectComponent(Type type)
    {
        int index = Array.IndexOf(Scene.ECS.GetComponentTypes(CameraEntity), type);
        Widget("entity_properties", "component").DropdownCallback(index);
        Inspector.SetDropdownValue("entity_properties", "component", index);
    }

    /// <summary>Selects position editing in the property inspector.</summary>
    private void SelectPosition()
    {
        var field = Widget("entity_properties", "field");
        int index = Array.IndexOf(field.DropdownOptions, "Position");
        field.DropdownCallback(index);
        Inspector.SetDropdownValue("entity_properties", "field", index);
    }

    /// <inheritdoc/>
    protected override void Update(GameTime time)
    {
        if (Navigation && Frame >= 25) InjectInput();
        base.Update(time);

        if (Navigation && Frame >= 25) return;

        if (Frame == 5)
        {
            if (TwoD) { CameraEntity = Scene.ECS.CreateEntity(); Scene.ECS.AddComponent<Camera2D>(CameraEntity); }
            else CameraEntity = ((CornellBoxScene)Scene).Meshes.CameraEntity;

            Inspector.SelectEntity(CameraEntity);
            Inspector.HideWindow("inspector");
            Inspector.HideWindow("perf");
            Inspector.HideWindow("geometry3d");
            Inspector.HideWindow("rendering2d");
            Inspector.OpenCameraControls(CameraEntity);
            Widget("camera_controls", "activateCamera").ButtonCallback();
        }

        if (Frame == 10)
        {
            SelectPosition();
            Widget("entity_properties", "value0").TextInputCallback("1.25");
            Inspector.ShowWindow("camera_controls");
        }

        if (Frame == 20)
        {
            if (TwoD) { CameraEntity = Scene.ECS.CreateEntity(Vector2.Zero); Scene.ECS.AddComponent<Camera2D>(CameraEntity); }
            else { CameraEntity = Scene.ECS.CreateEntity(new Vector3(0, 3, 10)); Scene.ECS.AddComponent<Camera3D>(CameraEntity); }

            Inspector.SelectEntity(CameraEntity);
            SelectComponent(TwoD ? typeof(Transform2D) : typeof(Transform3D));
            SelectPosition();
            Widget("entity_properties", "value0").TextInputCallback("2.25");
            Widget("entity_properties", "plus").ButtonCallback();
            Inspector.OpenCameraControls(CameraEntity);
            Widget("camera_controls", "activateCamera").ButtonCallback();
            Inspector.ShowWindow("camera_controls");
        }

        if (Frame == 30)
        {
            if (TwoD) ((Rendering2DScene)Scene).SelectRenderer(2); else ((CornellBoxScene)Scene).SelectRenderer(2);
        }

        if (Frame == 40) Widget("entity_properties", "value0").TextInputCallback("3.25");
        if (Frame == 50) Scene.ECS.ScheduleDestroy(CameraEntity);
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);
        Frame++;

        if (Navigation && Frame > 25)
        {
            if (Frame < 120) return;

            Capture("navigation");

            if (TwoD)
            {
                var transform = Scene.ECS.GetComponent<Transform2D>(CameraEntity);

                if (transform.Position.X < 3 || transform.Position.Y > -0.1f || Math.Abs(transform.Rotation) < 0.01f) throw new Exception($"2D editor input/navigation failed: {transform.Position}, rotation {transform.Rotation}.");
            }
            else
            {
                var transform = Scene.ECS.GetComponent<Transform3D>(CameraEntity);

                if (transform.Position.X < 3 || transform.Position.Z > 9.9f || transform.Rotation.Length() < 0.01f) throw new Exception($"3D editor input/navigation failed: {transform.Position}, rotation {transform.Rotation}.");
            }

            Console.WriteLine("PASS: controlled framework input for Ctrl+A numeric editing, Enter commit, camera toggle, RMB look and WASD movement.");
            Exit();

            return;
        }

        if (Frame == 15)
        {
            float x = TwoD ? Scene.ECS.GetComponent<Camera2D>(CameraEntity).Position.X : Scene.ECS.GetComponent<Camera3D>(CameraEntity).Position.X;

            if (x != 1.25f) throw new Exception("Scene overwrote live camera edits.");
            if (Inspector.SelectedEntity != CameraEntity) throw new Exception("Entity selection lost its camera binding.");

            Capture("camera");
        }

        if (Frame == 25)
        {
            if (TwoD)
            {
                var shape = ((Rendering2DScene)Scene).Geometry;

                if (shape.CameraEntity != CameraEntity || Math.Abs(shape.Camera.View.Translation.X + 2.35f) > 0.001f) throw new Exception("2D view did not follow the edited camera transform.");
            }
            else
            {
                var mesh = ((CornellBoxScene)Scene).Meshes;

                if (mesh.CameraEntity != CameraEntity || Math.Abs(mesh.Camera.Position.X - 2.35f) > 0.001f) throw new Exception($"3D view did not follow the edited camera transform: entity={mesh.CameraEntity}/{CameraEntity}, position={mesh.Camera.Position}, transform={Scene.ECS.GetComponent<Transform3D>(CameraEntity).Position}.");
            }

            Capture("transform");
        }

        var tracer = TwoD ? ((Rendering2DScene)Scene).Tracer : ((CornellBoxScene)Scene).Tracer;

        if (Frame == 41 && tracer.SampleCount != 1) throw new Exception("Live camera edits did not reset tracing history.");
        if (Frame == 46 && tracer.SampleCount < 5) throw new Exception("Camera edits kept resetting tracing history.");
        if (Frame != 55) return;
        if (Inspector.SelectedEntity != -1 || tracer.SampleCount != 0 || Inspector.IsWindowVisible("entity_properties")) throw new Exception("Deleted camera retained editor selection or tracing history.");

        Console.WriteLine($"PASS: {(TwoD ? "2D" : "3D")} generic entity/component editor, live values, steps, view camera selection, optional transforms, tracing invalidation and camera deletion.");
        Exit();
    }

    /// <summary>Drive the normal Inspector input path deterministically, without desktop focus/XTEST dependencies.</summary>
    private void InjectInput()
    {
        if (Frame == 44) Inspector.HideWindow("entity_inspector");

        float scale = (float)typeof(Inspector).GetProperty("UIScale", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var factor = Scene.Renderer.VirtualToScreenScale * scale;
        var value = Widget("entity_properties", "value0").Bounds.Center.ToVector2() * factor;
        var toggle = Widget("camera_controls", "navigateCamera").Bounds.Center.ToVector2() * factor;
        var keys = new List<Key>();
        if (Frame == 32) { keys.Add(Key.LeftControl); keys.Add(Key.A); }
        if (Frame >= 45 && Frame < 90) keys.Add(Key.W);

        int x = Frame <= 39 ? (int)value.X : Frame <= 43 ? (int)toggle.X : Frame < 47 ? 900 : 930;
        int y = Frame <= 39 ? (int)value.Y : Frame <= 43 ? (int)toggle.Y : Frame < 47 ? 670 : 685;
        var mouse = new MouseState(x, y, LeftButton: Frame is 30 or 42 ? ButtonState.Pressed : ButtonState.Released,
            RightButton: Frame >= 45 && Frame < 90 ? ButtonState.Pressed : ButtonState.Released);
        string text = Frame >= 34 && Frame <= 38 ? "3.25\r"[Frame - 34].ToString() : "";
        Input.State = new(new KeyboardState(keys.ToArray()), mouse, text);

    }

    /// <summary>Saves the rendered frame to the configured capture destination.</summary>
    private void Capture(string name)
    {
        Directory.CreateDirectory("artifacts/editor");

        var target = Scene.Renderer.SceneRT;
        using var stream = File.Create($"artifacts/editor/{(TwoD ? "2d" : "3d")}-{name}.png");
        target.SaveAsPng(stream, target.Width, target.Height);
    }
}
