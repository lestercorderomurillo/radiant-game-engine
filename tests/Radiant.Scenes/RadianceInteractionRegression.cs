using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.input;

namespace Radiant.Testing;

/// <summary>Replay actual brush input against the fitted renderer before and after resize.</summary>
internal static class RadianceInteractionRegression
{
    public static void Run(RadianceScene scene)
    {
        var controls = typeof(Scene).GetProperty("Controls");
        var inspector = scene.ECS.GetSystem<Inspector>();
        if (!inspector.Visible || inspector.CurrentWorkspaceMode != WorkspaceMode.Panels)
            throw new Exception("Debugging did not start in visible Panels mode.");
        var originalVisible = inspector.Visible;
        inspector.Visible = false;
        var originalInput = scene.Controls;
        float originalDelta = scene.DeltaTime;
        var renderer = scene.Renderer;
        var originalBounds = renderer.ScenePresentationBounds;
        InputSnapshot previous = default;
        var center = renderer.VirtualSize / 2;
        var entities = new List<int>();
        int Count() => scene.ECS.EntityCount;
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Input(Vector2 position, bool left = false, bool right = false, bool focused = true, params Key[] keys)
        {
            var current = new InputSnapshot(new KeyboardState(keys), new MouseState((int)position.X, (int)position.Y,
                LeftButton: left ? ButtonState.Pressed : ButtonState.Released,
                RightButton: right ? ButtonState.Pressed : ButtonState.Released), IsFocused: focused);
            controls.SetValue(scene, new InputFrame(current, previous));
            previous = current;
            scene.Update();
            scene.HandleInput();
        }
        void Reset()
        {
            Input(Vector2.Zero);
            Input(Vector2.Zero, keys: [Key.R]);
            Input(Vector2.Zero);
        }
        try
        {
            scene.DeltaTime = 0;
            Reset();
            int initial = Count();
            Require(initial == 163, "Radiance did not create twelve lights, 150 dots and one triangle.");
            scene.ECS.GetAllEntityIds(entities);
            var emitters = entities.Where(id => scene.ECS.GetComponent<Material>(id).Emissive.A > 0).ToArray();
            Require(emitters.Length == 12 && emitters.All(id =>
                MathF.Abs(Vector2.Distance(scene.ECS.GetComponent<Transform2D>(id).Position, center) - 300) < 0.1f),
                "The scene must have exactly twelve lights close to the center.");
            var dots = entities.Where(id => scene.ECS.HasComponent<Circle2D>(id) && !scene.ECS.HasComponent<MotionTrackable>(id)).ToArray();
            Require(dots.Length == 150 && dots.All(id => Vector2.Distance(scene.ECS.GetComponent<Transform2D>(id).Position, center) >= 550),
                "The dot rings do not leave the central lights clear.");
            int triangle = entities.Single(id => scene.ECS.HasComponent<Triangle2D>(id));
            Require(scene.ECS.GetComponent<Material>(triangle).Emissive.A == 0, "The central triangle emits light.");
            var screenCenter = renderer.WorldToScreen(center);
            Input(screenCenter, left: true);
            Require(Count() == initial + 1, "Left click did not place a dot.");
            for (int i = 0; i < 8; i++) Input(screenCenter, left: true);
            Require(Count() == initial + 1, "Holding the brush in place creates duplicate dots.");
            var end = renderer.WorldToScreen(center + new Vector2(160, 0));
            Input(end, left: true);
            Require(Count() == initial + 4, "Fast paint strokes leave gaps.");
            Input(end);
            Input(end, right: true);
            Input(screenCenter, right: true);
            Require(Count() == initial, "Fast erase strokes leave dots behind or erase moving lights.");

            Input(screenCenter);
            Input(screenCenter, left: true, keys: [Key.LeftShift]);
            scene.ECS.GetAllEntityIds(entities);
            int painted = entities.Single(id => !scene.ECS.HasComponent<MotionTrackable>(id)
                && Vector2.DistanceSquared(scene.ECS.GetComponent<Transform2D>(id).Position, center) < 2);
            Require(scene.ECS.GetComponent<Material>(painted).Emissive.A == 0, "Painting with Shift must not add lights.");
            Input(screenCenter);
            Input(screenCenter, right: true);
            Require(!scene.ECS.IsAlive(painted), "Painted dots cannot be erased.");

            int originalDot = entities.First(id => scene.ECS.IsAlive(id) && scene.ECS.HasComponent<Circle2D>(id) && !scene.ECS.HasComponent<MotionTrackable>(id));
            var dotPosition = renderer.WorldToScreen(scene.ECS.GetComponent<Transform2D>(originalDot).Position);
            Input(dotPosition);
            Input(dotPosition, right: true);
            Require(!scene.ECS.IsAlive(originalDot), "The initial field cannot be edited.");
            Reset();
            Require(Count() == initial, "Reset did not restore the complete field.");

            Input(screenCenter, left: true, focused: false);
            Input(new Vector2(-20, -20), left: true);
            Input(screenCenter, left: true, right: true);
            Require(Count() == initial, "Inactive, out-of-bounds, or conflicting input painted dots.");

            scene.ECS.GetAllEntityIds(entities);
            int light = entities.First(id => scene.ECS.HasComponent<MotionTrackable>(id));
            var position = scene.ECS.GetComponent<Transform2D>(light).Position;
            scene.DeltaTime = 0.5f;
            Input(screenCenter);
            Require(scene.ECS.GetComponent<Transform2D>(light).Position != position, "Lights do not animate.");
            Input(screenCenter, keys: [Key.Space]);
            position = scene.ECS.GetComponent<Transform2D>(light).Position;
            Input(screenCenter);
            Require(scene.ECS.GetComponent<Transform2D>(light).Position == position, "Space did not pause animation.");
            Input(screenCenter, keys: [Key.Space]);
            Input(screenCenter);
            Require(scene.ECS.GetComponent<Transform2D>(light).Position != position, "Space did not resume animation.");
            scene.DeltaTime = 0;

            // Inspector deletion recycles IDs: a newly painted dot must not be animated as a light.
            scene.ECS.DestroyEntity(light);
            Input(screenCenter);
            Input(screenCenter, left: true);
            position = scene.ECS.GetComponent<Transform2D>(light).Position;
            scene.DeltaTime = 0.5f;
            Input(screenCenter);
            Require(scene.ECS.GetComponent<Transform2D>(light).Position == position, "A recycled light ID moved a painted dot.");
            scene.DeltaTime = 0;
            Reset();

            renderer.ScenePresentationBounds = new Rectangle(120, 80, 600, 350);
            var dockCenter = new Vector2(420, 255);
            Input(dockCenter, left: true);
            scene.ECS.GetAllEntityIds(entities);
            Require(Count() == initial + 1 && entities.Any(id =>
                Vector2.DistanceSquared(scene.ECS.GetComponent<Transform2D>(id).Position, center) < 2),
                "Painting does not map to the docked game viewport.");
            Input(new Vector2(50, 50), left: true);
            Require(Count() == initial + 1, "Painting escaped the docked viewport.");
            renderer.ScenePresentationBounds = originalBounds;
            Reset();

            // Model a UI capture, including a jump into a floating panel on the click frame.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var visible = typeof(Inspector).GetField("GlobalVisible", flags);
            var overUI = typeof(Inspector).GetField("MouseOverUI", flags);
            var wasVisible = visible.GetValue(inspector);
            var wasOverUI = overUI.GetValue(inspector);
            try
            {
                visible.SetValue(inspector, true);
                overUI.SetValue(inspector, true);
                Input(screenCenter, left: true);
                Input(screenCenter, right: true);
                Require(Count() == initial, "Painting changed the field while Inspector captured the mouse.");
            }
            finally { visible.SetValue(inspector, wasVisible); overUI.SetValue(inspector, wasOverUI); }
            Console.WriteLine($"PASS: radiance paint/erase strokes, twelve central lights, nonemissive objects, reset, pause, recycled IDs and viewport/UI input at {renderer.ScreenSize}.");
        }
        finally
        {
            renderer.ScenePresentationBounds = originalBounds;
            scene.DeltaTime = originalDelta;
            controls.SetValue(scene, originalInput);
            inspector.Visible = originalVisible;
        }
    }
}
