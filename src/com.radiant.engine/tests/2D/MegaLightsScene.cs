using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using com.radiant.engine.input;
using System;
using System.Collections.Generic;

namespace com.radiant.engine.core;

/// <summary>Interactive HRC demo with animated lights, occluders, and mouse painting.</summary>
public class MegaLightsScene : Scene
{
    private int MouseLightId;
    private int[] RotatingLightIds;

    private MouseState PrevMouse;
    private Random Rng = new();

    private float Rotation;
    private float RainbowHue = 0f;
    private const float HueSpeed = 0.008f;
    private const float PaintRadius = 8f;
    private const float PaintSpacing = 3f; // Spacing between painted lights (half radius for overlap)
    private Vector2 LastPaintPos;
    private bool HasLastPaintPos = false;
    private Vector2 LastRightPaintPos;
    private bool HasLastRightPaintPos = false;
    private bool IsAnimating = true;
    // Z constants for layering (MaxZLayers = 65536, no offset)
    private const float RotatingLightZ = 65530f;  // Near top
    private const float MouseLightZ = 65535f;     // Always on top

    private Upscaling Upscaler;

    private const float RotationSpeed = 0.12f;
    private const float OrbitRadius = 360f;
    private const int LightCount = 14;

    private const float BoxSize = 80f;
    private const float ColumnSpacing = 120f;
    private const int MaxBoxesPerColumn = 10;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();

        var lighting = ECS.AddSystem<HRCGI>();
        Upscaler = DemoRendering.AddHrc(ECS, lighting);

        ECS.AddSystem<GizmosRenderer>();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        Renderer.FitWorld(Renderer.VirtualSize / 2, Renderer.VirtualSize);

        CreateRotatingLights();
        CreateOccluders();
        CreateMouseLight();
        CreateCenterTriangles();
    }

    /// <summary>Adds the nested emissive triangles used as central demo geometry.</summary>
    private void CreateCenterTriangles()
    {
        var center = Renderer.VirtualSize / 2;

        // Large outer triangle - black border
        float sizeOuter = 500f;
        CreateBorderedTriangle(center, sizeOuter, Color.Black);

        // Smaller inner triangle - purple emissive
        float sizeInner = 150f;
        CreateBorderedTriangle(center, sizeInner, new Color(180, 0, 255));
    }

    /// <summary>Creates a triangle with a dark border and emissive interior.</summary>
    private void CreateBorderedTriangle(Vector2 center, float size, Color emissive)
    {
        int id = ECS.CreateEntity();

        ECS.AddComponent<Transform2D>(id);
        ECS.AddComponent<Triangle2D>(id);
        ECS.AddComponent<Material>(id);

        ref var transform = ref ECS.GetComponent<Transform2D>(id);
        ref var triangle = ref ECS.GetComponent<Triangle2D>(id);
        ref var material = ref ECS.GetComponent<Material>(id);

        float centroidOffsetY = size * 0.117f;
        transform.Position = new Vector2(center.X - size / 2, center.Y - size / 2 - centroidOffsetY);
        transform.Layer = 1f;  // Layer 1
        transform.Scale = Vector2.One;

        triangle.Size = new Vector2(size);
        triangle.Bordered = true;

        material.Albedo = new Color(0, 0, 0, 200);
        material.Emissive = emissive;
    }

    /// <summary>Spawns the lights that orbit the center of the demo.</summary>
    private void CreateRotatingLights()
    {
        RotatingLightIds = new int[LightCount];

        var center = Renderer.VirtualSize / 2;

        for (int i = 0; i < LightCount; i++)
        {
            float angle = i / (float)LightCount * (global::System.MathF.PI * 2);
            float x = center.X + OrbitRadius * MathF.Cos(angle);
            float y = center.Y + OrbitRadius * MathF.Sin(angle);

            // Rainbow colors
            float hue = i / (float)LightCount;
            var color = HueToRGB(hue);

            RotatingLightIds[i] = CreateLight(new Vector2(x, y), 25f, color, color);

            // Add motion tracking for rotating lights (these move every frame)
            ECS.AddComponent<MotionTrackable>(RotatingLightIds[i]);
        }
    }

    /// <summary>Populates the demo with light-blocking geometry.</summary>
    private void CreateOccluders()
    {
        var screen = Renderer.VirtualSize;
        var center = Renderer.VirtualSize / 2;

        int columnsPerSide = (int)((screen.X / 2 - ColumnSpacing) / ColumnSpacing);
        float maxDistance = screen.X / 2f;  // Scale alpha across full screen half-width

        var occluderList = new List<int>();

        for (int col = 1; col <= columnsPerSide; col++)
        {
            float distanceFromCenter = col * ColumnSpacing;

            // Farther from center = more alpha, closer = less alpha
            float alpha = (distanceFromCenter / maxDistance) * 1.0f;
            alpha = Math.Clamp(alpha, 0f, 1.0f);

            byte alphaByte = (byte)(alpha * 255);

            // Each column farther gains +1 top and +1 bottom box
            int extraBoxes = col - 1;
            int boxesInColumn = MaxBoxesPerColumn + extraBoxes * 2;

            // Left column
            float leftX = center.X - distanceFromCenter;

            for (int row = 0; row < boxesInColumn; row++)
            {
                float y = center.Y - (boxesInColumn * BoxSize / 2) + row * BoxSize + BoxSize / 2;
                occluderList.Add(CreateOccluder(new Vector2(leftX, y), BoxSize, alphaByte));
            }

            // Right column
            float rightX = center.X + distanceFromCenter;

            for (int row = 0; row < boxesInColumn; row++)
            {
                float y = center.Y - (boxesInColumn * BoxSize / 2) + row * BoxSize + BoxSize / 2;
                occluderList.Add(CreateOccluder(new Vector2(rightX, y), BoxSize, alphaByte));
            }
        }
    }

    /// <summary>Creates the emissive entity that follows the cursor.</summary>
    private void CreateMouseLight()
    {
        var mouse = Controls.Mouse;
        var worldPos = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
        MouseLightId = CreateLight(worldPos, 100f, new Color(0, 0, 0, 128), new Color(0, 0, 0, 128));

        // Add motion tracking for mouse-controlled light
        ECS.AddComponent<MotionTrackable>(MouseLightId);

        PrevMouse = mouse;
    }

    /// <summary>Creates an emissive circle entity with the requested position and material.</summary>
    private int CreateLight(Vector2 position, float radius, Color color, Color emissive, float? z = null)
    {
        int id = ECS.CreateEntity();

        // Add all components first (each AddComponent moves entity to new archetype)
        ECS.AddComponent<Transform2D>(id);
        ECS.AddComponent<Circle2D>(id);
        ECS.AddComponent<Material>(id);

        // Now get fresh refs after entity is in final archetype
        ref var transform = ref ECS.GetComponent<Transform2D>(id);
        ref var circle = ref ECS.GetComponent<Circle2D>(id);
        ref var material = ref ECS.GetComponent<Material>(id);

        // Use entity ID as Z by default (unique, monotonic = newer always on top)
        transform.Position = position;
        transform.Layer = z ?? id;
        transform.Scale = Vector2.One;

        material.Albedo = color;
        material.Emissive = emissive;

        circle.Radius = radius;

        return id;
    }

    /// <summary>Creates a light-blocking entity with the requested geometry and material.</summary>
    private int CreateOccluder(Vector2 position, float size)
    {
        int id = ECS.CreateEntity();

        ECS.AddComponent<Transform2D>(id);
        ECS.AddComponent<Rectangle2D>(id);
        ECS.AddComponent<Material>(id);

        ref var transform = ref ECS.GetComponent<Transform2D>(id);
        ref var rect = ref ECS.GetComponent<Rectangle2D>(id);
        ref var material = ref ECS.GetComponent<Material>(id);

        transform.Position = position;
        transform.Layer = 0f;  // Layer 0 (back)
        transform.Scale = Vector2.One;

        rect.Size = new Vector2(size);

        material.Albedo = new Color(30, 30, 0, 90);
        material.Emissive = Color.Black;

        return id;
    }

    /// <summary>Creates a light-blocking entity with the requested geometry and material.</summary>
    private int CreateOccluder(Vector2 position, float size, byte alpha)
    {
        int id = ECS.CreateEntity();

        ECS.AddComponent<Transform2D>(id);
        ECS.AddComponent<Rectangle2D>(id);
        ECS.AddComponent<Material>(id);

        ref var transform = ref ECS.GetComponent<Transform2D>(id);
        ref var rect = ref ECS.GetComponent<Rectangle2D>(id);
        ref var material = ref ECS.GetComponent<Material>(id);

        transform.Position = position;
        transform.Layer = 0f;  // Layer 0 (back)
        transform.Scale = Vector2.One;

        rect.Size = new Vector2(size);

        material.Albedo = new Color((byte)0, (byte)0, (byte)0, alpha);
        material.Emissive = Color.Transparent;

        return id;
    }

    /// <inheritdoc/>
    public override void Update()
    {
        var mouse = Controls.Mouse;
        var center = Renderer.VirtualSize / 2;

        // Space: toggle animation
        if (Controls.WasPressed(Key.Space))
            IsAnimating = !IsAnimating;

        // Animate rotating lights
        if (IsAnimating)
            Rotation += RotationSpeed * DeltaTime;

        for (int i = 0; i < RotatingLightIds.Length; i++)
        {
            float angle = i / (float)LightCount * (global::System.MathF.PI * 2) + Rotation;
            float x = center.X + OrbitRadius * MathF.Cos(angle);
            float y = center.Y + OrbitRadius * MathF.Sin(angle);

            ref var transform = ref ECS.GetComponent<Transform2D>(RotatingLightIds[i]);
            transform.Position = new Vector2(x, y);
            transform.Layer = RotatingLightZ;
        }

        // Update mouse light (always on top) - convert screen coords to virtual world coords
        var mouseWorld = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
        ref var mouseTransform = ref ECS.GetComponent<Transform2D>(MouseLightId);
        mouseTransform.Position = mouseWorld;
        mouseTransform.Layer = MouseLightZ;

        // Only allow spawning when window is focused
        if (Renderer.Window.IsActive)
        {
            var mousePos = mouseWorld;

            bool leftDown = mouse.LeftButton == ButtonState.Pressed;
            bool rightDown = mouse.RightButton == ButtonState.Pressed;

            // Left click: rainbow lights (blocked if right is also held)
            if (leftDown && !rightDown)
            {
                if (!HasLastPaintPos)
                {
                    PaintLightAt(mousePos);
                    LastPaintPos = mousePos;
                    HasLastPaintPos = true;
                }
                else
                {
                    float distance = Vector2.Distance(LastPaintPos, mousePos);

                    if (distance >= PaintSpacing)
                    {
                        Vector2 direction = Vector2.Normalize(mousePos - LastPaintPos);
                        float traveled = PaintSpacing;

                        while (traveled <= distance)
                        {
                            PaintLightAt(LastPaintPos + direction * traveled);
                            traveled += PaintSpacing;
                        }

                        LastPaintPos = mousePos;
                    }
                }
            }
            else
            {
                HasLastPaintPos = false;
            }

            // Right click: black dots (blocked if left is also held)
            if (rightDown && !leftDown)
            {
                if (!HasLastRightPaintPos)
                {
                    PaintBlackDotAt(mousePos);
                    LastRightPaintPos = mousePos;
                    HasLastRightPaintPos = true;
                }
                else
                {
                    float distance = Vector2.Distance(LastRightPaintPos, mousePos);

                    if (distance >= PaintSpacing)
                    {
                        Vector2 direction = Vector2.Normalize(mousePos - LastRightPaintPos);
                        float traveled = PaintSpacing;

                        while (traveled <= distance)
                        {
                            PaintBlackDotAt(LastRightPaintPos + direction * traveled);
                            traveled += PaintSpacing;
                        }

                        LastRightPaintPos = mousePos;
                    }
                }
            }
            else
            {
                HasLastRightPaintPos = false;
            }
        }

        // F11 to toggle UDR mode
        if (Controls.WasPressed(Key.F11))
            Upscaler.Select((UpscalingMode)(((int)Upscaler.Mode + 1) % 5));

        // X to spawn 100,000 random debug entities
        if (Controls.WasPressed(Key.X))
            SpawnDebugEntities(100_000);

        PrevMouse = mouse;
    }

    /// <summary>Adds a colored emissive mark at the world-space brush position.</summary>
    private void PaintLightAt(Vector2 position)
    {
        var color = HueToRGB(RainbowHue);
        RainbowHue = (RainbowHue + HueSpeed) % 1f;

        var nearby = ECS.InRadius(new Vector3(position, 0), PaintRadius);

        foreach (int entityId in nearby)
        {
            if (ECS.HasComponent<Circle2D>(entityId) && entityId != MouseLightId)
            {
                ref var material = ref ECS.GetComponent<Material>(entityId);
                material.Albedo = color;
                material.Emissive = color;

                return;
            }
        }

        // Z defaults to entity ID (unique, newer = higher = on top)
        CreateLight(position, PaintRadius, color, color);
    }

    /// <summary>Adds a dark occluding mark at the world-space brush position.</summary>
    private void PaintBlackDotAt(Vector2 position)
    {
        var color = Color.Black;

        var nearby = ECS.InRadius(new Vector3(position, 0), PaintRadius);

        foreach (int entityId in nearby)
        {
            if (ECS.HasComponent<Circle2D>(entityId) && entityId != MouseLightId)
            {
                ref var material = ref ECS.GetComponent<Material>(entityId);
                material.Albedo = color;
                material.Emissive = color;

                return;
            }
        }

        CreateLight(position, PaintRadius, color, Color.Black);
    }

    /// <summary>Converts a normalized hue into a fully saturated RGB color.</summary>
    private static Color HueToRGB(float hue)
    {
        float r = MathF.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(hue * 6f - 2f);
        float b = 2f - MathF.Abs(hue * 6f - 4f);

        return new Color(
            (byte)(Math.Clamp(r, 0f, 1f) * 255),
            (byte)(Math.Clamp(g, 0f, 1f) * 255),
            (byte)(Math.Clamp(b, 0f, 1f) * 255)
        );
    }

    /// <summary>Adds a batch of entities for inspector and performance experiments.</summary>
    private void SpawnDebugEntities(int count)
    {
        var screen = Renderer.VirtualSize;

        for (int i = 0; i < count; i++)
        {
            float x = (float)Rng.NextDouble() * screen.X;
            float y = (float)Rng.NextDouble() * screen.Y;
            var color = HueToRGB((float)Rng.NextDouble());

            // Z defaults to entity ID (unique, newer = higher = on top)
            CreateLight(new Vector2(x, y), 3f, color, color);
        }
    }

    /// <inheritdoc/>
    public override void Render()
    {
        base.Render();
    }
}
