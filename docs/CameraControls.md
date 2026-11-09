# Camera controls

Fly through any scene while it runs: pick a camera, hold the right mouse button and move. No camera code needed.

## Open the controller

Press **F1**, then choose **ECS → Open Camera Controls** (or **View → Camera Controls**). From code:

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

## Make your camera controllable

Any entity with a `Camera2D` or `Camera3D` appears in the list. Give it a `Transform2D` or `Transform3D` to drive
its position and rotation from the transform; the camera component keeps the lens settings (zoom, field of view,
clip range).
