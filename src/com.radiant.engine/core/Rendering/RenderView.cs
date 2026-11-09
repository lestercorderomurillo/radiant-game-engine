using System;

namespace com.radiant.engine.core;

/// <summary>Backend-independent viewport metrics, resolution scale and fitted 2D coordinates.</summary>
public sealed class RenderView
{
    /// <summary>Current viewport width in pixels.</summary>
    public int ScreenWidth { get; private set; }

    /// <summary>Current viewport height in pixels.</summary>
    public int ScreenHeight { get; private set; }

    /// <summary>Current viewport size as Vector2.</summary>
    public Vector2 ScreenSize { get; private set; }

    /// <summary>Width / Height ratio.</summary>
    public float AspectRatio { get; private set; }

    /// <summary>Height / Width ratio.</summary>
    public float InverseAspectRatio { get; private set; }

    /// <summary>Virtual resolution width (fixed world coordinate space).</summary>
    public float VirtualWidth { get; private set; }

    /// <summary>Virtual resolution height (fixed world coordinate space).</summary>
    public float VirtualHeight { get; private set; }

    /// <summary>Virtual resolution as Vector2 (fixed world coordinate space).</summary>
    public Vector2 VirtualSize { get; private set; }

    /// <summary>Diagonal length of the screen in pixels.</summary>
    public float ScreenDiagonal { get; private set; }

    /// <summary>Total pixel count (Width * Height).</summary>
    public int ScreenArea { get; private set; }

    /// <summary>Largest power of 2 that fits within max(Width, Height).</summary>
    public int ScreenLowerPowerOfTwo { get; private set; }

    /// <summary>Smallest power of 2 that contains max(Width, Height).</summary>
    public int ScreenHigherPowerOfTwo { get; private set; }

    /// <summary>Square Vector2 using ScreenLowerPowerOfTwo.</summary>
    public Vector2 ScreenSizeLowerPowerOfTwo { get; private set; }

    /// <summary>Square Vector2 using ScreenHigherPowerOfTwo.</summary>
    public Vector2 ScreenSizeHigherPowerOfTwo { get; private set; }

    /// <summary>Scale factor from virtual coordinates to screen pixels (ScreenSize / VirtualSize).</summary>
    public Vector2 VirtualToScreenScale { get; private set; }

    private float RenderScaleValue = 1.0f;

    /// <summary>
    /// Render scale factor for dynamic resolution (0.25 to 1.0).
    /// Systems can subscribe to RenderScaleChanged to resize their render targets.
    /// </summary>
    public float RenderScale
    {
        get => RenderScaleValue;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            value = Math.Clamp(value, 0.25f, 1.0f);
            if (Math.Abs(RenderScaleValue - value) > 0.001f)
            {
                RenderScaleValue = Math.Clamp(value, 0.25f, 1.0f);
                UpdateScaledScreenInfo();
                RenderScaleChanged?.Invoke(RenderScaleValue);
            }
        }
    }

    /// <summary>Fired when RenderScale changes. Parameter is the new scale value.</summary>
    public event Action<float> RenderScaleChanged;

    /// <summary>Scaled viewport width (ScreenWidth * RenderScale).</summary>
    public int ScaledWidth { get; private set; }

    /// <summary>Scaled viewport height (ScreenHeight * RenderScale).</summary>
    public int ScaledHeight { get; private set; }

    /// <summary>Scaled viewport size as Vector2.</summary>
    public Vector2 ScaledSize { get; private set; }

    /// <summary>Smallest power of 2 containing max(ScaledWidth, ScaledHeight).</summary>
    public int ScaledHigherPowerOfTwo { get; private set; }


    /// <summary>World-to-clip transform for shape geometry.</summary>
    public Matrix ViewProjection { get; private set; }

    public RenderView(int width, int height, float virtualWidth = 3840, float virtualHeight = 2160)
    {
        if (!float.IsFinite(virtualWidth) || virtualWidth <= 0) throw new ArgumentOutOfRangeException(nameof(virtualWidth));
        if (!float.IsFinite(virtualHeight) || virtualHeight <= 0) throw new ArgumentOutOfRangeException(nameof(virtualHeight));
        VirtualWidth = virtualWidth;
        VirtualHeight = virtualHeight;
        VirtualSize = new Vector2(virtualWidth, virtualHeight);
        Resize(width, height);
    }

    /// <summary>Updates all metrics before returning. Invalid dimensions preserve the previous view.</summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || width > (1 << 30)) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > (1 << 30) || (long)width * height > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));
        ScreenWidth = width;
        ScreenHeight = height;
        ScreenSize = new Vector2(ScreenWidth, ScreenHeight);
        AspectRatio = (float)ScreenWidth / ScreenHeight;
        InverseAspectRatio = (float)ScreenHeight / ScreenWidth;
        ScreenDiagonal = MathF.Sqrt((float)ScreenWidth * ScreenWidth + (float)ScreenHeight * ScreenHeight);
        ScreenArea = ScreenWidth * ScreenHeight;

        int maxDimension = Math.Max(ScreenWidth, ScreenHeight);
        ScreenLowerPowerOfTwo = GetLowerPowerOfTwo(maxDimension);
        ScreenHigherPowerOfTwo = GetHigherPowerOfTwo(maxDimension);
        ScreenSizeLowerPowerOfTwo = new Vector2(ScreenLowerPowerOfTwo, ScreenLowerPowerOfTwo);
        ScreenSizeHigherPowerOfTwo = new Vector2(ScreenHigherPowerOfTwo, ScreenHigherPowerOfTwo);
        VirtualToScreenScale = new Vector2(ScreenWidth / VirtualWidth, ScreenHeight / VirtualHeight);
        UpdateWorldView();
        UpdateScaledScreenInfo();
    }

    /// <summary>Refreshes screen dimensions derived from the current render scale.</summary>
    private void UpdateScaledScreenInfo()
    {
        ScaledWidth = Math.Max(1, (int)(ScreenWidth * RenderScaleValue));
        ScaledHeight = Math.Max(1, (int)(ScreenHeight * RenderScaleValue));
        ScaledSize = new Vector2(ScaledWidth, ScaledHeight);

        int scaledMax = Math.Max(ScaledWidth, ScaledHeight);
        ScaledHigherPowerOfTwo = GetHigherPowerOfTwo(scaledMax);
    }

    /// <summary>Returns the largest power of two no greater than the supplied value.</summary>
    private static int GetLowerPowerOfTwo(int value)
    {
        if (value <= 0) return 1;

        int power = 1;

        while (power <= value / 2)
            power *= 2;

        return power;
    }

    /// <summary>Returns the smallest power of two no less than the supplied value.</summary>
    private static int GetHigherPowerOfTwo(int value)
    {
        if (value <= 0) return 1;

        int power = 1;

        while (power < value)
            power *= 2;

        return power;
    }

    /// <summary>
    /// Converts screen-space coordinates (e.g. mouse position) to virtual world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        return (screenPos - WorldOffset) / WorldScale;
    }

    /// <summary>
    /// Converts virtual world coordinates to screen-space coordinates.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        return worldPos * WorldScale + WorldOffset;
    }

    /// <summary>
    /// Converts a virtual-coordinate rectangle to a screen-pixel Rectangle with edge-snapping.
    /// Each edge is rounded independently so adjacent rectangles sharing a virtual edge
    /// map to the exact same screen pixel — no sub-pixel gaps.
    /// </summary>
    public Rectangle VirtualToScreenRect(float x, float y, float width, float height)
    {
        var Start = WorldToScreen(new Vector2(x, y));
        var End = WorldToScreen(new Vector2(x + width, y + height));
        int px = (int)MathF.Round(Start.X);
        int py = (int)MathF.Round(Start.Y);
        int pr = (int)MathF.Round(End.X);
        int pb = (int)MathF.Round(End.Y);

        return new Rectangle(px, py, pr - px, pb - py);
    }

    private Vector2? FittedWorldCenter;
    private Vector2 FittedWorldSize;
    private Vector2 WorldScale;
    private Vector2 WorldOffset;

    /// <summary>Transforms world-space sprites into screen pixels, including the fitted view.</summary>
    public Matrix WorldTransform { get; private set; }

    /// <summary>
    /// Keeps the given world area centered and fully visible without stretching.
    /// The fit updates automatically when the window resizes; gameplay coordinates stay unchanged.
    /// </summary>
    public void FitWorld(Vector2 Center, Vector2 Size)
    {
        if (!float.IsFinite(Center.X) || !float.IsFinite(Center.Y)) throw new ArgumentOutOfRangeException(nameof(Center));
        if (!float.IsFinite(Size.X) || !float.IsFinite(Size.Y) || Size.X <= 0 || Size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(Size));

        FittedWorldCenter = Center;
        FittedWorldSize = Size;
        UpdateWorldView();
    }

    /// <summary>Returns the current fitted 2D world view for shared camera tools.</summary>
    public bool TryGetWorldFit(out Vector2 Center, out Vector2 Size)
    {
        Center = FittedWorldCenter.GetValueOrDefault();
        Size = FittedWorldSize;
        return FittedWorldCenter.HasValue;
    }

    /// <summary>Recalculates the world-to-screen transform for the fitted view.</summary>
    private void UpdateWorldView()
    {
        WorldScale = VirtualToScreenScale;
        WorldOffset = Vector2.Zero;

        if (FittedWorldCenter.HasValue)
        {
            float Scale = MathF.Min(ScreenWidth / FittedWorldSize.X, ScreenHeight / FittedWorldSize.Y);
            WorldScale = new Vector2(Scale);
            WorldOffset = ScreenSize / 2f - FittedWorldCenter.Value * Scale;
        }

        WorldTransform = Matrix.CreateScale(WorldScale.X, WorldScale.Y, 1f)
            * Matrix.CreateTranslation(WorldOffset.X, WorldOffset.Y, 0f);
        ViewProjection = WorldTransform
            * Matrix.CreateOrthographicOffCenter(0, ScreenWidth, ScreenHeight, 0, 0, 1);
    }
}
