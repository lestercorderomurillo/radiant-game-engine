# Shared camera controls

Open **F1 > ECS > Open Camera Controls** or **View > Camera Controls**. One window serves the whole scene; inspecting another entity does not change the camera being controlled.

Search by entity ID or camera type, then select a camera instance. The Render view selector lists compatible `ICameraView` system instances. **Use selected camera** binds that view to the selected camera. Navigation edits the camera without changing other view bindings. Disabled cameras cannot be activated or moved; missing cameras and empty search results stop navigation.

Enable **Navigate camera**, then hold the right mouse button outside inspector windows: WASD moves, Q/E moves vertically in 3D, mouse movement looks, and Shift increases speed. Movement speed uses world units per second. Closing the controller or hiding the inspector suspends navigation.

Scenes using `Renderer.FitWorld` also expose **World view (fitted 2D)**. WASD pans this view; Zoom in/out keeps its center and aspect ratio. Scene code can restore its framing by calling `FitWorld` again. Pac-Man and Zuma expose a Camera controls button in their scene settings.

## Integration

Register `Inspector` as usual. No per-camera windows or game-specific controller systems are required:

```csharp
Inspector.OpenCameraControls();       // Browse the scene's cameras.
Inspector.OpenCameraControls(entity); // Select a camera explicitly.
```

Calls are safe when no Inspector is registered. Entity selection does not activate a render view automatically. Camera entities are discovered from `Camera2D` and `Camera3D`; compatible render views implement `ICameraView`. Fitted-world tools read the current framing through `Renderer.TryGetWorldFit(out center, out size)` and edit it through `FitWorld`.

Headless controller regressions run with `dotnet run --project tests/Radiant.Tests -c Release`. They cover discovery, multiple view instances, selection persistence, independent property inspection, filtering, empty scenes/results, deletion, and disabled cameras.
