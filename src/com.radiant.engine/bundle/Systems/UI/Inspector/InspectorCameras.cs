using System;
using System.Globalization;
using System.Linq;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private bool NavigatingCamera;
    private float NavigationSpeed = 5;
    private MouseState NavigationMouse;
    private ICameraView[] CameraViews = Array.Empty<ICameraView>();
    private int CameraViewIndex;
    private void AddCameraControls()
    {
        if (!Scene.ECS.IsAlive(InspectedEntity)) return;
        var previousView = CameraViews.ElementAtOrDefault(CameraViewIndex);
        CameraViews = Scene.ECS.GetAllSystems().OfType<ICameraView>().Where(view => Scene.ECS.TryGetComponent(InspectedEntity, view.CameraType, out _)).ToArray();
        if (CameraViews.Length == 0) return;
        var preferredType = PropertyType == typeof(Camera2D) || PropertyType == typeof(Transform2D) ? typeof(Camera2D) : PropertyType == typeof(Camera3D) || PropertyType == typeof(Transform3D) ? typeof(Camera3D) : previousView?.CameraType;
        CameraViewIndex = previousView?.CameraType == preferredType ? Array.IndexOf(CameraViews, previousView) : -1;
        if (CameraViewIndex < 0) CameraViewIndex = Math.Max(0, Array.FindIndex(CameraViews, view => view.CameraType == preferredType));
        AddButton("entity_properties", "cameraControls", "Camera controls", () => ShowWindow("camera_controls"));
        CreateWindow("camera_controls", "Camera Controls", 54, Visible: false);
        var window = Windows["camera_controls"];
        window.Widgets.Clear(); window.WidgetIndex.Clear();
        window.Title = $"Camera {InspectedEntity} Controls";
        AddLabel("camera_controls", "pose", "");
        AddDropdown("camera_controls", "cameraView", "Camera view", CameraViews.Select((view, index) => $"{view.GetType().Name} [{index}]").ToArray(), CameraViewIndex, index => { CameraViewIndex = index; RefreshCameraPose(); });
        AddButton("camera_controls", "activateCamera", "Use selected camera", () => CameraViews[CameraViewIndex].CameraEntity = InspectedEntity);
        AddToggle("camera_controls", "navigateCamera", "Navigate camera", NavigatingCamera, enabled => NavigatingCamera = enabled);
        AddLabel("camera_controls", "speedLabel", "Movement speed (units / second)");
        AddTextInput("camera_controls", "speed", "Movement speed", text => { if (float.TryParse(text, CultureInfo.InvariantCulture, out float speed) && float.IsFinite(speed) && speed > 0) NavigationSpeed = speed; });
        MarkNumericInput("camera_controls", "speed");
        SetTextInputValue("camera_controls", "speed", NavigationSpeed.ToString(CultureInfo.InvariantCulture));
        AddLabel("camera_controls", "navigationHelp", "Hold RMB: look + WASD / Q E. Shift: fast.");
        RefreshCameraPose();
    }
    private void RefreshCameraPose()
    {
        var type = CameraViews[CameraViewIndex].CameraType;
        string pose = type == typeof(Camera3D) && Scene.ECS.HasComponent<Transform3D>(InspectedEntity) ? "Transform3D" : type == typeof(Camera2D) && Scene.ECS.HasComponent<Transform2D>(InspectedEntity) ? "Transform2D" : type.Name;
        SetLabel("camera_controls", "pose", $"Edit position / rotation in {pose}.");
        bool navigable = type == typeof(Camera3D) || type == typeof(Camera2D);
        SetWidgetEnabled("camera_controls", "navigateCamera", navigable);
        if (!navigable) { NavigatingCamera = false; SetToggleValue("camera_controls", "navigateCamera", false); }
    }

    private void UpdateCameraNavigation()
    {
        var mouse = Mouse.GetState();
        var previous = NavigationMouse; NavigationMouse = mouse;
        if (!GlobalVisible || !NavigatingCamera || !Renderer.IsActive || IsEditingText || !Scene.ECS.IsAlive(InspectedEntity) || Scene.ECS.IsDisabled(InspectedEntity) || mouse.RightButton != ButtonState.Pressed) return;
        var cursor = ScreenToVirtual(new Vector2(mouse.X, mouse.Y));
        if (cursor.Y < MenuBarHeight || Windows.Values.Any(window => window.Visible && window.WindowBounds.Contains(cursor)) || OpenDropdownWindowId != null || OpenMenuId != null) return;
        var keyboard = Keyboard.GetState();
        float distance = NavigationSpeed * Math.Min(Scene.DeltaTime, 0.1f) * (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? 4 : 1);
        var look = previous.RightButton == ButtonState.Pressed ? new Vector2(mouse.X - previous.X, mouse.Y - previous.Y) * 0.003f : Vector2.Zero;
        var movement = new Vector3((keyboard.IsKeyDown(Keys.D) ? 1 : 0) - (keyboard.IsKeyDown(Keys.A) ? 1 : 0), (keyboard.IsKeyDown(Keys.E) ? 1 : 0) - (keyboard.IsKeyDown(Keys.Q) ? 1 : 0), (keyboard.IsKeyDown(Keys.S) ? 1 : 0) - (keyboard.IsKeyDown(Keys.W) ? 1 : 0));
        if (movement.LengthSquared() > 1) movement.Normalize();
        CameraEditor.Move(Scene.ECS, InspectedEntity, movement, look, distance, CameraViews[CameraViewIndex].CameraType);
    }
}
