using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Geometry sampling modes for multisampling and supersampling.</summary>
public enum RasterAAMode { None, MSAA2x, MSAA4x, MSAA8x, SSAA2x, SSAA3x }

/// <summary>Per-view geometry sampling, independent of post AA. Share with the view's geometry adapters.</summary>
[SystemTag("Rendering", "AA")]
public class RasterSampler : core.System
{
    public RasterAAMode Mode;
    public Vector2 Jitter;
    public Func<Vector2> TemporalJitter;
    public int ActualSamples { get; internal set; }
    public string Status => !Enabled ? "Disabled" : RequestedSamples > 0 ? ActualSamples > 0 ? $"{Mode} (actual {ActualSamples}x)" : $"{Mode} (inactive)" : Mode.ToString();
    public int Scale => Enabled ? Mode == RasterAAMode.SSAA2x ? 2 : Mode == RasterAAMode.SSAA3x ? 3 : 1 : 1;
    public int RequestedSamples => !Enabled ? 0 : Mode switch { RasterAAMode.MSAA2x => 2, RasterAAMode.MSAA4x => 4, RasterAAMode.MSAA8x => 8, _ => 0 };

    /// <summary>Calculates raster target dimensions for the selected sampling mode.</summary>
    public Point RenderSize(Point size) => new(checked(size.X * Scale), checked(size.Y * Scale));

    /// <summary>Applies the sampling jitter to a projection matrix.</summary>
    public Matrix Projection(Matrix projection, Point size)
    {
        var jitter = Jitter + (TemporalJitter?.Invoke() ?? Vector2.Zero);

        if (!Enabled || jitter == Vector2.Zero) return projection;

        return projection * Matrix.CreateTranslation(2 * jitter.X / size.X, -2 * jitter.Y / size.Y, 0);
    }
}
