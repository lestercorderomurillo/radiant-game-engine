using System;
using System.Globalization;
using System.Linq;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using com.radiant.engine.input;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private bool NavigatingCamera;
    private float NavigationSpeed = 5;
    private MouseState NavigationMouse;
    private ICameraView[] CameraViews = Array.Empty<ICameraView>();
    private int CameraViewIndex;
    private int ControlledCamera = -1;
    private string CameraSearch = "";
    private int[] CameraEntities = [];
    private string[] CameraLabels = [];
    private readonly System.Collections.Generic.List<int> CameraCandidates = new();

    /// <summary>Opens the shared camera controller, optionally selecting an entity independently of the property inspector.</summary>
    public static void OpenCameraControls(int Entity = -1)
    {
        if (Instance == null) return;

        Instance.EnsureCameraControls();

        if (Entity >= 0)
        {
            Instance.ControlledCamera = Entity;
            Instance.CameraSearch = "";
            SetTextInputValue("camera_controls", "search", "");
        }

        Instance.RefreshCameraControls();
        ShowWindow("camera_controls");
    }

    /// <summary>Adds the controls for camera selection, movement, and zoom.</summary>
    private void AddCameraControls()
    {
        if (!Scene.ECS.IsAlive(InspectedEntity)) return;

        if (Scene.ECS.HasComponent<Camera2D>(InspectedEntity) || Scene.ECS.HasComponent<Camera3D>(InspectedEntity))
        {
            int entity = InspectedEntity;
            AddButton("entity_properties", "cameraControls", "Camera controls", () => OpenCameraControls(entity));
        }
    }

    /// <summary>Creates the shared camera-control window if it does not yet exist.</summary>
    private void EnsureCameraControls()
    {
        if (Windows.ContainsKey("camera_controls")) return;

        CreateWindow("camera_controls", "Camera Controls", 54, Visible: false);
        AddTextInput("camera_controls", "search", "Search camera ID or type", text => { CameraSearch = text; RefreshCameraControls(); });
        AddDropdown("camera_controls", "camera", "Camera instance", [], 0, index =>
        {
            if (index < 0 || index >= CameraEntities.Length) return;

            ControlledCamera = CameraEntities[index];
            NavigatingCamera = false;
            SetToggleValue("camera_controls", "navigateCamera", false);
            RefreshCameraControls();
        });
        AddLabel("camera_controls", "pose", "");
        AddDropdown("camera_controls", "cameraView", "Render view", [], 0, index => { CameraViewIndex = index; RefreshCameraPose(); });
        AddButton("camera_controls", "activateCamera", "Use selected camera", () =>
        {
            if (CameraViewIndex >= 0 && CameraViewIndex < CameraViews.Length && Scene.ECS.IsAlive(ControlledCamera) && !Scene.ECS.IsDisabled(ControlledCamera)
                && Scene.ECS.TryGetComponent(ControlledCamera, CameraViews[CameraViewIndex].CameraType, out _))
                CameraViews[CameraViewIndex].CameraEntity = ControlledCamera;
        });
        AddToggle("camera_controls", "navigateCamera", "Navigate camera", false, enabled => NavigatingCamera = enabled);
        AddLabel("camera_controls", "speedLabel", "Movement speed (units / second)");
        AddTextInput("camera_controls", "speed", "Movement speed", text => { if (float.TryParse(text, CultureInfo.InvariantCulture, out float speed) && float.IsFinite(speed) && speed > 0) NavigationSpeed = speed; });
        MarkNumericInput("camera_controls", "speed");
        SetTextInputValue("camera_controls", "speed", NavigationSpeed.ToString(CultureInfo.InvariantCulture));
        AddButton("camera_controls", "zoomIn", "Zoom in", () => ZoomWorldCamera(0.8f));
        AddButton("camera_controls", "zoomOut", "Zoom out", () => ZoomWorldCamera(1.25f));
        AddLabel("camera_controls", "navigationHelp", "Hold RMB: look + WASD / Q E. Shift: fast.");
    }

    /// <summary>Refreshes searchable cameras and preserves a valid selection.</summary>
    private void RefreshCameraControls()
    {
        Scene.ECS.GetAllEntityIds(CameraCandidates);

        var candidates = CameraCandidates.Where(entity => Scene.ECS.HasComponent<Camera2D>(entity) || Scene.ECS.HasComponent<Camera3D>(entity))
            .ToList();
        if (Renderer.TryGetWorldFit(out _, out _)) candidates.Insert(0, -2);

        var entities = candidates.Where(entity => CameraLabel(entity).Contains(CameraSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
        var labels = entities.Select(CameraLabel).ToArray();

        if (!CameraEntities.SequenceEqual(entities) || !CameraLabels.SequenceEqual(labels))
        {
            CameraEntities = entities;
            CameraLabels = labels;
            SetDropdownOptions("camera_controls", "camera", labels);
        }

        if (!CameraEntities.Contains(ControlledCamera))
        {
            ControlledCamera = CameraEntities.FirstOrDefault(-1);
            NavigatingCamera = false;
            SetToggleValue("camera_controls", "navigateCamera", false);
        }

        SetDropdownValue("camera_controls", "camera", Math.Max(0, Array.IndexOf(CameraEntities, ControlledCamera)));
        SetWidgetEnabled("camera_controls", "camera", CameraEntities.Length > 0);

        var previousView = CameraViews.ElementAtOrDefault(CameraViewIndex);
        var views = Scene.ECS.GetAllSystems().OfType<ICameraView>()
            .Where(view => ControlledCamera >= 0 && Scene.ECS.TryGetComponent(ControlledCamera, view.CameraType, out _)).ToArray();

        if (!CameraViews.SequenceEqual(views))
        {
            CameraViews = views;
            CameraViewIndex = Math.Max(0, Array.IndexOf(CameraViews, previousView));
            SetDropdownOptions("camera_controls", "cameraView", views.Select((view, index) => $"{view.GetType().Name} [{index}]").ToArray());
            SetDropdownValue("camera_controls", "cameraView", CameraViewIndex);
        }

        RefreshCameraPose();
    }

    /// <summary>Formats the entity ID and camera type for the camera list.</summary>
    private string CameraLabel(int Entity)
        => Entity == -2 ? "World view (fitted 2D)" : $"Entity {Entity} — {(Scene.ECS.HasComponent<Camera2D>(Entity) ? "Camera2D" : "")}{(Scene.ECS.HasComponent<Camera3D>(Entity) ? " Camera3D" : "")}{(Scene.ECS.IsDisabled(Entity) ? " (disabled)" : "")}";

    private Type ControlledCameraType => CameraViews.ElementAtOrDefault(CameraViewIndex)?.CameraType
        ?? (ControlledCamera >= 0 && Scene.ECS.HasComponent<Camera3D>(ControlledCamera) ? typeof(Camera3D) : typeof(Camera2D));

    /// <summary>Synchronizes pose controls with the selected camera or fitted world view.</summary>
    private void RefreshCameraPose()
    {
        bool worldView = ControlledCamera == -2 && Renderer.TryGetWorldFit(out _, out _);
        bool available = worldView || ControlledCamera >= 0 && Scene.ECS.IsAlive(ControlledCamera) && !Scene.ECS.IsDisabled(ControlledCamera);
        var type = ControlledCameraType;
        string pose = ControlledCamera >= 0 && type == typeof(Camera3D) && Scene.ECS.HasComponent<Transform3D>(ControlledCamera) ? "Transform3D" : ControlledCamera >= 0 && type == typeof(Camera2D) && Scene.ECS.HasComponent<Transform2D>(ControlledCamera) ? "Transform2D" : type.Name;
        SetLabel("camera_controls", "pose", worldView ? "Fitted world view. WASD pans; use buttons to zoom." : ControlledCamera < 0 ? "No matching camera instances in this scene." : !available ? "Selected camera is disabled." : $"Edit position / rotation in {pose}.");
        SetWidgetEnabled("camera_controls", "zoomIn", worldView);
        SetWidgetEnabled("camera_controls", "zoomOut", worldView);
        SetWidgetEnabled("camera_controls", "cameraView", available && CameraViews.Length > 0);
        SetWidgetEnabled("camera_controls", "activateCamera", available && CameraViews.Length > 0);
        SetWidgetEnabled("camera_controls", "navigateCamera", available);

        if (!available) { NavigatingCamera = false; SetToggleValue("camera_controls", "navigateCamera", false); }
    }

    /// <summary>Changes the fitted world view's zoom while preserving its center.</summary>
    private void ZoomWorldCamera(float Scale)
    {
        if (ControlledCamera == -2 && Renderer.TryGetWorldFit(out var center, out var size))
        {
            var scaled = size * Scale;
            if (float.IsFinite(scaled.X) && float.IsFinite(scaled.Y) && scaled.X >= 1 && scaled.Y >= 1) Renderer.FitWorld(center, scaled);
        }
    }

    /// <summary>Applies navigation input to the selected camera while controls are active.</summary>
    private void UpdateCameraNavigation()
    {
        if (IsWindowVisible("camera_controls")) RefreshCameraControls();

        var mouse = Controls.Mouse;
        var previous = NavigationMouse;
        NavigationMouse = mouse;

        if (!GlobalVisible || !IsWindowVisible("camera_controls") || !NavigatingCamera || !Renderer.IsActive || IsEditingText || (ControlledCamera != -2 && (!Scene.ECS.IsAlive(ControlledCamera) || Scene.ECS.IsDisabled(ControlledCamera))) || mouse.RightButton != ButtonState.Pressed) return;

        var cursor = ScreenToVirtual(new Vector2(mouse.X, mouse.Y));

        if (cursor.Y < MenuBarHeight || Windows.Values.Any(window => window.Visible && window.WindowBounds.Contains(cursor)) || OpenDropdownWindowId != null || OpenMenuId != null) return;

        var keyboard = Controls.Keyboard;
        float distance = NavigationSpeed * Math.Min(Scene.DeltaTime, 0.1f) * (keyboard.IsKeyDown(Key.LeftShift) || keyboard.IsKeyDown(Key.RightShift) ? 4 : 1);
        var look = previous.RightButton == ButtonState.Pressed ? new Vector2(mouse.X - previous.X, mouse.Y - previous.Y) * 0.003f : Vector2.Zero;
        var movement = new Vector3((keyboard.IsKeyDown(Key.D) ? 1 : 0) - (keyboard.IsKeyDown(Key.A) ? 1 : 0), (keyboard.IsKeyDown(Key.E) ? 1 : 0) - (keyboard.IsKeyDown(Key.Q) ? 1 : 0), (keyboard.IsKeyDown(Key.S) ? 1 : 0) - (keyboard.IsKeyDown(Key.W) ? 1 : 0));
        if (movement.LengthSquared() > 1) movement = Vector3.Normalize(movement);

        if (ControlledCamera == -2)
        {
            if (Renderer.TryGetWorldFit(out var center, out var size)) Renderer.FitWorld(center + new Vector2(movement.X, movement.Z) * distance, size);
        }
        else CameraEditor.Move(Scene.ECS, ControlledCamera, movement, look, distance, ControlledCameraType);
    }
}
