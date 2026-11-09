# Input

Every scene and system reads the keyboard, mouse and typed text through `Controls`. All callbacks in a frame see
the same snapshot, so a press is never missed or counted twice.

## Read keys and buttons

The following example shows the three ways to read a key or mouse button:

```csharp
using com.radiant.engine.input;

public override void Update()
{
    if (Controls.WasPressed(Key.Space)) Jump();            // Only on the frame it goes down
    if (Controls.WasReleased(MouseButton.Left)) Drop();    // Only on the frame it comes up
    if (Controls.IsDown(Key.LeftShift)) Sprint();          // Every frame while held
}
```

Read presses, releases and text in `Update`. Read held keys in `FixedUpdate`, where movement and physics run.

## Use the mouse

The following example shows how to place something under the cursor, drag a camera and zoom with the wheel:

```csharp
Vector2 world = Renderer.ScreenToWorld(Controls.Mouse.Position); // Window pixels → world
if (Controls.IsDown(MouseButton.Right)) camera.Position -= Controls.MouseDelta / camera.Zoom;
camera.Zoom *= 1f + Controls.ScrollDelta / 1200f;                // 120 units per wheel notch
```

When the inspector is open, check `Inspector.IsMouseOverUI()` before reacting to world clicks.

## Read typed text

`Controls.Text` holds the characters typed since the last frame, already respecting the keyboard layout and
modifiers.

The following example shows a simple chat box:

```csharp
foreach (char character in Controls.Text)
{
    if (character == '\b' && message.Length > 0) message = message[..^1];
    else if (!char.IsControl(character)) message += character;
}
```

## Focus

When the window loses focus, held keys and buttons read as released and typed text is dropped, so characters
don't keep walking off-screen. A key still held when focus returns counts as a fresh press.

## Script or replay input

Pass your own `IInputSource` to `GameClient` to feed recorded or generated input, which is ideal for tests and
replays.

The following example shows how to run 120 simulated frames with scripted input:

```csharp
new GameClient(new HeadlessBackend(120, TimeSpan.FromSeconds(1.0 / 60)), myInput)
    .Run(new SimulationScene());
```

Your source returns an `InputSnapshot` (keyboard, mouse, text and focus) from `Capture()` once per frame. Return
each typed character only once.

Gamepads, touch and IME composition are not available.
