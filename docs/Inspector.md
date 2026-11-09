# Inspector

Build in-game tuning and debug panels in a few lines: sliders, toggles, dropdowns, text fields and lists inside
draggable windows. Press **F1** to show or hide them.

Add the [`Inspector`](../src/com.radiant.engine/bundle/Systems/UI/Inspector/Inspector.cs) system to your scene.
Use its static widget helpers to build panels, then keep diagnostic overlays in the Gizmos section below.

## Windows and widgets

Create windows and widgets once (in `Initialize` or `SetupScene`), then update their values every frame.

Create a lighting panel with controls bound to your game state:

```csharp
Inspector.CreateWindow("lighting", "Lighting");
Inspector.AddLabel("lighting", "fps", "FPS: --");
Inspector.AddSlider("lighting", "radius", "Radius", 10f, 400f, 100f, value => Radius = value);
Inspector.AddToggle("lighting", "shadows", "Shadows", true, enabled => Shadows = enabled);
Inspector.AddDropdown("lighting", "quality", "Quality", ["Low", "Medium", "High"], 1, index => Quality = index);
Inspector.AddButton("lighting", "reset", "Reset", Reset);
```

Update the label during the frame:

```csharp
public override void Update()
{
    Inspector.SetLabel("lighting", "fps", $"FPS: {1f / Time.DeltaSeconds:0}");
}
```

Setters mirror each widget: `SetSliderValue`, `SetToggleValue`, `SetDropdownValue`, `SetDropdownOptions`,
`SetTextInputValue` and `SetListBoxItems`. `SetWidgetEnabled` greys out a widget, and `RemoveWidget` or
`DestroyWindow` removes it.

### Inline rows

Widgets with an inline ratio share a row. The following example shows how to build a search bar from a text
field and a button; empty button text shows a search icon:

```csharp
Inspector.AddTextInput("entities", "query", "Search…", text => Filter = text, 0.8f);
Inspector.AddButton("entities", "go", "", Search, 0.2f);
```

### List boxes

`AddListBox` shows a scrollable, selectable list. `SetListBoxHeader` adds a fixed header row (columns are
separated by `"  |  "`), `GetListBoxSelected` returns the selected indices, and `ClearListBoxSelection` resets them.

## Visibility and layout

`ShowWindow`, `HideWindow`, `ToggleWindow` and `IsWindowVisible` control windows by ID. Windows are
repositioned only when the game window is resized, the UI scale changes, or **View → Reorder Windows** runs,
which also raises `Inspector.WindowsRestored`.

The following example shows how to keep world clicks and shortcuts from firing through the UI:

```csharp
if (Controls.WasPressed(MouseButton.Left) && !Inspector.IsMouseOverUI()) Select(cursor);
if (!Inspector.IsEditingText && Controls.WasPressed(Key.R)) Restart();
```

## Menu bar

- **File** — close the application.
- **ECS** — entity, component and system inspectors, plus the shared camera controls.
- **View** — every registered window, with Show All, Hide All and Reorder Windows.
- **Help** — About Radiant.

`Inspector.OpenCameraControls()` opens the [camera controller](CameraControls.md) directly.

## Themes

Built-in themes: Carbon (default), Solaris, Midnight, Sentinel (light), Greenfields, Neon and Nord. Register
your own with `RegisterTheme(name, theme)` and switch with `ApplyTheme(name)`.

## Draw gizmos

[`GizmosRenderer`](../src/com.radiant.engine/bundle/Systems/UI/Gizmos/GizmosRenderer.cs) draws diagnostic
shapes and labels over the scene. The following example shows how to queue gizmos from a system's `Render`:

```csharp
var gizmos = ECS.GetSystem<GizmosRenderer>();
gizmos.AddGizmoLine(start, end, Color.Gold, thickness: 2f);
gizmos.AddGizmoCircle(center, radius, Color.Lime);
gizmos.AddGizmoRect(bounds, Color.Red, filled: false);
gizmos.AddGizmoText(position, "spawn", Color.White);
gizmos.ToggleGizmos();
```

`PerformanceMonitor` adds frame-time, CPU and GPU metrics windows; GPU counters are available on Windows only.

## Continue

- [Input](Input.md) — read controls and respect UI focus.
- [Cameras](CameraControls.md) — navigate the scene while inspecting it.
- [Image quality](RenderingPipeline.md) — inspect lighting and reconstruction buffers.
