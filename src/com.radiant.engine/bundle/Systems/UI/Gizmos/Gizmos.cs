
namespace com.radiant.engine.bundle;

/// <summary>Endpoints, color, and thickness of a queued diagnostic line.</summary>
public struct LineGizmo
{
    public Vector2 Start;
    public Vector2 End;
    public Color Color;
    public float Thickness;

    /// <summary>Stores endpoints and appearance for a diagnostic line.</summary>
    public LineGizmo(Vector2 start, Vector2 end, Color color, float thickness = 1f)
    {
        Start = start;
        End = end;
        Color = color;
        Thickness = thickness;
    }
}

/// <summary>Position, radius, and appearance of a queued diagnostic circle.</summary>
public struct CircleGizmo
{
    public Vector2 Center;
    public float Radius;
    public Color Color;

    /// <summary>Stores center, radius, and appearance for a diagnostic circle.</summary>
    public CircleGizmo(Vector2 center, float radius, Color color)
    {
        Center = center;
        Radius = radius;
        Color = color;
    }
}

/// <summary>Position, angular span, and appearance of a queued diagnostic arc.</summary>
public struct ArcGizmo
{
    public Vector2 Center;
    public float Radius;
    public float StartAngle;
    public float EndAngle;
    public Color Color;

    /// <summary>Stores the center, angular range, and appearance for a diagnostic arc.</summary>
    public ArcGizmo(Vector2 center, float radius, float startAngle, float endAngle, Color color)
    {
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        EndAngle = endAngle;
        Color = color;
    }
}

/// <summary>Text, position, and appearance of a queued diagnostic label.</summary>
public struct TextGizmo
{
    public Vector2 Position;
    public string Text;
    public Color Color;

    /// <summary>Stores text, position, and appearance for a diagnostic label.</summary>
    public TextGizmo(Vector2 position, string text, Color color)
    {
        Position = position;
        Text = text;
        Color = color;
    }
}

/// <summary>Bounds and appearance of a queued diagnostic rectangle.</summary>
public struct RectGizmo
{
    public Rectangle Rect;
    public Color Color;
    public bool Filled;

    /// <summary>Stores bounds and appearance for a diagnostic rectangle.</summary>
    public RectGizmo(Rectangle rect, Color color, bool filled = false)
    {
        Rect = rect;
        Color = color;
        Filled = filled;
    }
}
