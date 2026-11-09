using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>
/// How an entity looks and interacts with light. <c>Emissive</c> makes it a light source; <c>Albedo</c> decides how
/// much light it blocks and what color the light takes when passing through.
/// </summary>
/// <example>
/// The following example shows a light, a solid wall and a pane of red glass:
/// <code>
/// ECS.AddComponent&lt;Material&gt;(lamp).Emissive = new Color(255, 200, 120);       // Alpha sets brightness
/// ECS.AddComponent&lt;Material&gt;(wall).Albedo = Color.Black;                      // Blocks all light
/// ECS.AddComponent&lt;Material&gt;(glass).Albedo = new Color(255, 60, 60, 160);   // Tints light red
/// </code>
/// </example>
[ComponentDescription("Surface color, emission and texture for rendering.")]
public struct Material : Component
{
    private Color AlbedoColor;
    private Color EmissiveColor;

    /// <summary>Optional texture that patterns the emitted color. Leave null for a solid color.</summary>
    public Texture2D Texture;

    /// <summary>Surface color. Darker colors block more light and the alpha sets how strongly; white lets light through untouched.</summary>
    public Color Albedo
    {
        readonly get => AlbedoColor;
        set { AlbedoColor = value; UpdateCached(); }
    }

    /// <summary>Light emitted by the surface. The alpha is the brightness; black emits nothing.</summary>
    public Color Emissive
    {
        readonly get => EmissiveColor;
        set { EmissiveColor = value; UpdateCached(); }
    }

    /// <summary>How much light the surface blocks, computed from <c>Albedo</c> and <c>Emissive</c>. Read-only.</summary>
    public Color Absorption { get; private set; }

    /// <summary><c>Emissive</c> with its brightness applied, as the lighting system uses it. Read-only.</summary>
    public Color EmissiveScaled { get; private set; }

    /// <summary>Creates a white, non-emissive material.</summary>
    public Material()
    {
        AlbedoColor = Color.White;
        EmissiveColor = Color.Black;
        Absorption = new Color(0, 0, 0, 255);
        EmissiveScaled = Color.Black;
    }

    /// <summary>Refreshes material values cached for rendering.</summary>
    private void UpdateCached()
    {
        float intensity = EmissiveColor.A / 255f;
        EmissiveScaled = new Color(
            (int)(EmissiveColor.R * intensity),
            (int)(EmissiveColor.G * intensity),
            (int)(EmissiveColor.B * intensity),
            EmissiveColor.A);

        bool isEmissive = EmissiveColor.R > 0 || EmissiveColor.G > 0 || EmissiveColor.B > 0;

        if (isEmissive)
        {
            Absorption = new Color(
                (int)(EmissiveColor.R * intensity),
                (int)(EmissiveColor.G * intensity),
                (int)(EmissiveColor.B * intensity),
                AlbedoColor.A);
        }
        else
        {
            float alpha = AlbedoColor.A / 255f;
            Absorption = new Color(
                (int)((255 - AlbedoColor.R) * alpha),
                (int)((255 - AlbedoColor.G) * alpha),
                (int)((255 - AlbedoColor.B) * alpha),
                AlbedoColor.A);
        }
    }
}
