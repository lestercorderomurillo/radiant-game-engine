using System;
using System.Collections.Generic;
using com.radiant.engine.bundle;
using com.radiant.engine.input;

namespace com.radiant.engine.core;

/// <summary>Twelve central orbiting lights, an unlit triangle, and 150 dots in spaced rings.</summary>
public class RadianceScene : Scene
{
    private const int LightCount = 12;
    private const int MaxDots = 4096;
    private const float DotRadius = 18f;
    private const float BrushSpacing = 48f;
    private readonly List<int> Dots = new();
    private readonly List<int> Lights = new();
    private Vector2 Center;
    private Vector2? LastBrushPosition;
    private int LastBrushMode;
    private float Phase;
    private bool Animating = true;
    private Upscaler Upscaler;

    // Ownership survives inspector edits and prevents recycled entity IDs from moving
    // a painted dot as a light, or deleting an unrelated entity during reset.
    private struct DemoDot : Component { }
    private int Triangle = -1;
    private struct DemoTriangle : Component { }
    private struct DemoLight : Component { public int Index; }

    [RunAfter(typeof(Inspector))]
    private sealed class BrushInput : System
    {
        public override void Update() => ((RadianceScene)Scene).HandleInput();
    }

    /// <inheritdoc/>
    public override void SetupECS()
    {
        ECS.AddSystemCollection(SystemCollections.Debugging);
        ECS.AddSystem<Geometry>();
        ECS.AddSystem<BrushInput>();
        var lighting = ECS.AddSystem<HRCGI>();
        Upscaler = DemoRendering.AddHrc(ECS, lighting);
        ECS.GetSystem<ToneMapper>().Exposure = 2.7f;
        ECS.GetSystem<ToneMapper>().Transfer = ColorTransfer.SRGB;
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        Center = Renderer.VirtualSize / 2;
        Renderer.FitWorld(Center, Renderer.VirtualSize);
        ResetField();
    }

    private void ResetField()
    {
        foreach (int id in Dots)
            if (ECS.IsAlive(id) && ECS.HasComponent<DemoDot>(id)) ECS.DestroyEntity(id);
        foreach (int id in Lights)
            if (ECS.IsAlive(id) && ECS.HasComponent<DemoLight>(id)) ECS.DestroyEntity(id);
        if (Triangle >= 0 && ECS.IsAlive(Triangle) && ECS.HasComponent<DemoTriangle>(Triangle)) ECS.DestroyEntity(Triangle);
        Dots.Clear();
        Lights.Clear();
        Phase = 0;
        LastBrushPosition = null;

        Triangle = ECS.CreateEntity();
        ECS.AddComponent<Transform2D>(Triangle) = new()
        {
            Position = Center - new Vector2(180, 222), Scale = Vector2.One, Layer = 2
        };
        ECS.AddComponent<Triangle2D>(Triangle) = new() { Size = new Vector2(360) };
        ECS.AddComponent<Material>(Triangle) = new() { Albedo = Color.Black, Emissive = Color.Transparent };
        ECS.AddComponent<DemoTriangle>(Triangle);

        // Five staggered rings leave a broad clear area around the central lights.
        // More dots on each larger ring keeps the spacing nearly uniform.
        for (int ring = 0; ring < 5; ring++)
        {
            int count = 18 + ring * 6;
            float radius = 560 + ring * 110;
            for (int dot = 0; dot < count; dot++)
            {
                float angle = MathF.Tau * (dot + ring * 0.37f) / count;
                var position = Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                AddDot(position, 22);
            }
        }

        for (int i = 0; i < LightCount; i++)
        {
            var tint = LightFactory.HueToRGB(i / (float)LightCount);
            var emission = new Color(tint.R, tint.G, tint.B, (byte)180);
            int id = LightFactory.CreateLight(ECS, LightPosition(i), 36, tint, emission, 1);
            ECS.AddComponent<DemoLight>(id) = new() { Index = i };
            ECS.AddComponent<MotionTrackable>(id);
            Lights.Add(id);
        }
    }

    private Vector2 LightPosition(int index)
    {
        float angle = index * MathF.Tau / LightCount + Phase * 0.32f;
        return Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 300;
    }

    /// <inheritdoc/>
    public override void Update()
    {
        if (Animating) Phase += DeltaTime;
        foreach (int id in Lights)
        {
            if (!ECS.IsAlive(id) || !ECS.HasComponent<DemoLight>(id) || !ECS.HasComponent<Transform2D>(id)) continue;
            ECS.SetPosition(id, LightPosition(ECS.GetComponent<DemoLight>(id).Index));
        }
    }

    // Run after Inspector has handled this frame, so clicks cannot paint through UI.
    internal virtual void HandleInput()
    {
        bool worldInput = Controls.IsFocused && !Inspector.IsMouseOverUI() && !Inspector.IsEditingText && !Inspector.IsCameraInputActive;
        if (worldInput)
        {
            if (Controls.WasPressed(Key.Space)) Animating = !Animating;
            if (Controls.WasPressed(Key.R)) ResetField();
            if (Controls.WasPressed(Key.F11)) Upscaler.Select((UpscalerMode)(((int)Upscaler.Mode + 1) % 5));
        }

        var pointer = Controls.Mouse.Position;
        var bounds = Renderer.ScenePresentationBounds ?? new Rectangle(0, 0, Renderer.ScreenWidth, Renderer.ScreenHeight);
        var position = Renderer.ScreenToWorld(pointer);
        bool inside = bounds.Contains((int)pointer.X, (int)pointer.Y)
            && position.X >= DotRadius && position.Y >= DotRadius
            && position.X <= Renderer.VirtualSize.X - DotRadius && position.Y <= Renderer.VirtualSize.Y - DotRadius;
        bool left = Controls.IsDown(MouseButton.Left), right = Controls.IsDown(MouseButton.Right);
        int mode = right ? 2 : 1;
        if (!worldInput || !inside || left == right)
        {
            LastBrushPosition = null;
            return;
        }

        Dots.RemoveAll(id => !ECS.IsAlive(id) || !ECS.HasComponent<DemoDot>(id));
        var start = LastBrushPosition.HasValue && mode == LastBrushMode ? LastBrushPosition.Value : position;
        LastBrushMode = mode;
        if (right)
        {
            EraseDots(start, position);
            LastBrushPosition = position;
        }
        else if (!LastBrushPosition.HasValue || start == position)
        {
            PlaceDot(position);
            LastBrushPosition = position;
        }
        else
        {
            // Keep the unconsumed distance so slow drags and fast drags paint equally.
            float distance = Vector2.Distance(start, position);
            var direction = (position - start) / distance;
            int steps = (int)(distance / BrushSpacing);
            for (int step = 1; step <= steps; step++) PlaceDot(start + direction * (step * BrushSpacing));
            LastBrushPosition = start + direction * (steps * BrushSpacing);
        }
    }

    private void PlaceDot(Vector2 position)
    {
        foreach (int id in Dots)
            if (ECS.HasComponent<Transform2D>(id)
                && Vector2.DistanceSquared(ECS.GetComponent<Transform2D>(id).Position, position) < BrushSpacing * BrushSpacing * 0.64f)
                return;
        if (Dots.Count < MaxDots) AddDot(position, DotRadius);
    }

    private void AddDot(Vector2 position, float radius)
    {
        int id = LightFactory.CreateLight(ECS, position, radius, Color.Black, Color.Transparent, 2);
        ECS.AddComponent<DemoDot>(id);
        Dots.Add(id);
    }

    private void EraseDots(Vector2 start, Vector2 end)
    {
        var segment = end - start;
        float lengthSquared = segment.LengthSquared();
        for (int i = Dots.Count - 1; i >= 0; i--)
        {
            int id = Dots[i];
            if (!ECS.HasComponent<Transform2D>(id) || !ECS.HasComponent<Circle2D>(id)) continue;
            var position = ECS.GetComponent<Transform2D>(id).Position;
            float t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(position - start, segment) / lengthSquared, 0, 1) : 0;
            float radius = DotRadius * 2 + ECS.GetComponent<Circle2D>(id).Radius;
            if (Vector2.DistanceSquared(position, start + segment * t) > radius * radius) continue;
            ECS.DestroyEntity(id);
            Dots.RemoveAt(i);
        }
    }
}
