using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>One-line helpers for adding lights to a 2D scene lit by <c>HRCGI</c>.</summary>
/// <example>
/// The following example shows how to add a warm lamp and a hundred colorful sparks:
/// <code>
/// var warm = new Color(255, 180, 100);
/// LightFactory.CreateLight(ECS, new Vector2(1920, 1080), 60f, warm, warm);
/// LightFactory.SpawnRandom(ECS, 100, Renderer.VirtualSize, radius: 4f);
/// </code>
/// </example>
public static class LightFactory
{
    /// <summary>Creates a glowing circle and returns its entity. <paramref name="emissive"/> is the light color (its alpha is brightness); <paramref name="z"/> sets the draw layer.</summary>
    public static int CreateLight(ECS ecs, Vector2 position, float radius,
        Color albedo, Color emissive, float? z = null, Texture2D texture = null)
    {
        int id = ecs.CreateEntity();
        ecs.AddComponent<Transform2D>(id);
        ecs.AddComponent<Circle2D>(id);
        ecs.AddComponent<Material>(id);

        ref var transform = ref ecs.GetComponent<Transform2D>(id);
        ref var circle = ref ecs.GetComponent<Circle2D>(id);
        ref var material = ref ecs.GetComponent<Material>(id);

        transform.Position = position;
        transform.Layer = z ?? id;
        transform.Scale = Vector2.One;
        circle.Radius = radius;
        material.Albedo = albedo;
        material.Emissive = emissive;
        material.Texture = texture;

        return id;
    }

    /// <summary>Scatters small lights of random hues across an area and returns their entities. Great for stress tests and ambience.</summary>
    public static int[] SpawnRandom(ECS ecs, int count, Vector2 screenSize, float radius = 3f)
    {
        var ids = new int[count];
        var rng = new Random();

        for (int i = 0; i < count; i++)
        {
            float x = (float)rng.NextDouble() * screenSize.X;
            float y = (float)rng.NextDouble() * screenSize.Y;
            var color = HueToRGB((float)rng.NextDouble());
            ids[i] = CreateLight(ecs, new Vector2(x, y), radius, color, color);
        }

        return ids;
    }

    /// <summary>Turns a hue from 0 to 1 into a fully saturated color, for rainbow effects.</summary>
    public static Color HueToRGB(float hue)
    {
        float r = MathF.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(hue * 6f - 2f);
        float b = 2f - MathF.Abs(hue * 6f - 4f);

        return new Color(
            (byte)(Math.Clamp(r, 0f, 1f) * 255),
            (byte)(Math.Clamp(g, 0f, 1f) * 255),
            (byte)(Math.Clamp(b, 0f, 1f) * 255));
    }
}
