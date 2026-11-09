using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using com.radiant.engine.input;
using com.radiant.engine.runtime;

internal static class InputRegression
{
    public static void Run()
    {
        // Snapshots own values, not the mutable array passed by a backend.
        var keys = new[] { Key.W, Key.F24, Key.OemBackslash };
        var keyboard = new KeyboardState(keys);
        keys[0] = Key.A;
        Require(keyboard.IsKeyDown(Key.W) && keyboard.IsKeyUp(Key.A), "Keyboard snapshot aliases caller storage");
        foreach (var key in Enum.GetValues<Key>())
        {
            var single = new KeyboardState(key);
            foreach (var other in Enum.GetValues<Key>())
                Require(single.IsKeyDown(other) == (other == key && key != Key.None), "Key bits overlap");
        }
        Require(!keyboard.IsKeyDown((Key)10000), "Unknown key accepted");

        var snapshots = new[]
        {
            new InputSnapshot(default, new MouseState(10, 20)),
            new InputSnapshot(new KeyboardState(Key.W), new MouseState(12, 23, 120, ButtonState.Pressed), "ñ🙂"),
            new InputSnapshot(new KeyboardState(Key.W), new MouseState(12, 23, 120, ButtonState.Pressed)),
            new InputSnapshot(new KeyboardState(Key.W), new MouseState(900, 900, 240, ButtonState.Pressed), "discard", false),
            new InputSnapshot(default, new MouseState(1, 1, 360)),
        };
        var source = new SequenceInput(snapshots);
        var scene = new InputScene();
        new GameClient(new HeadlessBackend(snapshots.Length, TimeSpan.FromSeconds(1.0 / 32)), source).Run(scene);
        Require(source.Captures == snapshots.Length, "Input was captured more than once per variable update");
        Require(scene.Probe.FixedCalls == snapshots.Length * 2, "Unexpected fixed callbacks");
        var frames = scene.Probe.Frames;
        Require(frames[1].WasPressed(Key.W) && frames[1].WasPressed(MouseButton.Left), "Press edges missing");
        Require(frames[1].MouseDelta == new global::System.Numerics.Vector2(2, 3) && frames[1].ScrollDelta == 120, "Mouse delta incorrect");
        Require(frames[1].Text == "ñ🙂" && frames[2].Text == "", "Committed Unicode text delivery");
        Require(frames[2].IsDown(Key.W) && !frames[2].WasPressed(Key.W), "Held key repeats press edge");
        Require(frames[3].WasReleased(Key.W) && frames[3].WasReleased(MouseButton.Left) && frames[3].Text == "", "Focus loss retains held input/text");
        Require(frames[3].MouseDelta == default && frames[4].MouseDelta == default && frames[4].ScrollDelta == 0, "Focus change causes mouse jump");

        // Render must use the preceding simulation snapshot, never poll the devices again.
        var renderSource = new SequenceInput([snapshots[1]]);
        using var loop = new GameLoop { InputSource = renderSource };
        var rendered = new InputScene();
        loop.Initialize();
        loop.SetTargetFps(0);
        loop.AddScene(rendered);
        loop.Update(TimeSpan.FromSeconds(1.0 / 64));
        loop.Render();
        Require(renderSource.Captures == 1 && rendered.Probe.RenderFrame == rendered.Controls, "Render recaptured input");
        Console.WriteLine("PASS: Radiant input snapshots, key coverage, shared fixed/update/render input, focus, edges, wheel, Unicode and headless injection.");
    }

    private static void Require(bool Value, string Message) { if (!Value) throw new Exception(Message); }
    private sealed class SequenceInput(InputSnapshot[] Frames) : IInputSource
    {
        public int Captures;
        public InputSnapshot Capture() => Frames[Captures++];
    }
    private sealed class InputScene : Scene
    {
        public InputProbe Probe;
        public override void SetupECS() { Probe = ECS.AddSystem(new InputProbe()); base.SetupECS(); }
    }
    private sealed class InputProbe : com.radiant.engine.core.System
    {
        public List<InputFrame> Frames = new();
        public int FixedCalls;
        private InputFrame FixedFrame;
        public InputFrame RenderFrame;
        public override void FixedUpdate() { FixedCalls++; FixedFrame = Controls; }
        public override void Update()
        {
            Require(ReferenceEquals(FixedFrame, Controls) && ReferenceEquals(Scene.Controls, Controls), "Systems do not share one immutable frame");
            Frames.Add(Controls);
        }
        public override void Render() => RenderFrame = Controls;
    }
}
