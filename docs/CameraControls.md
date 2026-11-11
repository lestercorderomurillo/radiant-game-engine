# Cameras

Fly through any scene while it runs: pick a camera, hold the right mouse button and move. No camera code needed.

## Open the controller

Register `ECS.AddSystemCollection(SystemCollections.Debugging)` to include the shared controller.
It opens with the other debug panels by default. Choose **ECS → Camera Controls** (or **View → Camera Controls**)
to reopen it; **F1** hides or restores the workspace. From code:

```csharp
Inspector.OpenCameraControls();          // Browse the scene's cameras
Inspector.OpenCameraControls(cameraId);  // Start with a specific camera selected
```

## Navigate

1. Search by entity ID or camera type and select a camera.
2. If the scene has several views, choose one under **Render view** and click **Use selected camera**.
3. Enable **Navigate camera**, then hold the right mouse button outside inspector windows.

| Input | Action |
| --- | --- |
| W A S D | Move |
| Q / E | Move down / up (3D) |
| Mouse | Look around |
| Shift | Move faster |

Every renderer (flat, raster and path tracing) follows the camera immediately. Closing the window or hiding the
inspector stops navigation.

## 2D scenes

Scenes framed with `Renderer.FitWorld` show a **World view (fitted 2D)** entry. WASD pans it and the zoom
buttons zoom around its center. Call `FitWorld` again from your scene to restore its original framing.

Tileset exposes **Tile viewport (2D)** for panning; zoom and rotation are unavailable for that renderer.
Renderer-owned viewports can implement `IViewportCamera2D` to join the same controls without creating
a camera entity. Pixel-based viewports start at 500 units/second; entity cameras start at 5.
Both speeds can be edited. Gameplay brushes should check `Inspector.IsCameraInputActive` so navigating
does not also place or erase objects.

## Make your camera controllable

Any entity with a `Camera2D` or `Camera3D` appears in the list. Give it a `Transform2D` or `Transform3D` to drive
its position and rotation from the transform; the camera component keeps the lens settings (zoom, field of view,
clip range).
