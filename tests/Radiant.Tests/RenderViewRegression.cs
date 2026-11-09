using System;
using com.radiant.engine.core;

internal static class RenderViewRegression
{
    public static void Run()
    {
        var view = new RenderView(1280, 720);
        Equal(view.WorldToScreen(new Vector2(1920, 1080)), new Vector2(640, 360), "Default virtual center");
        Require(!view.TryGetWorldFit(out _, out _), "Unexpected initial fit");
        view.FitWorld(new Vector2(100, 50), new Vector2(200, 100));
        Equal(view.WorldToScreen(Vector2.Zero), new Vector2(0, 40), "Letterbox top edge");
        Equal(view.WorldToScreen(new Vector2(200, 100)), new Vector2(1280, 680), "Letterbox bottom edge");
        view.Resize(960, 960);
        Equal(view.WorldToScreen(Vector2.Zero), new Vector2(0, 240), "Resize must retain fit");
        Require(view.TryGetWorldFit(out var center, out var size) && center == new Vector2(100, 50) && size == new Vector2(200, 100), "Fit state lost");
        var position = new Vector2(37.25f, 89.75f);
        Equal(view.ScreenToWorld(view.WorldToScreen(position)), position, "World/screen inverse");
        var transformed = Vector4.Transform(new Vector4(position, 0, 1), view.WorldTransform);
        Equal(new Vector2(transformed.X, transformed.Y), view.WorldToScreen(position), "Sprite transform disagrees with pointer mapping");
        var clip = Vector4.Transform(new Vector4(center, 0, 1), view.ViewProjection);
        Equal(new Vector2(clip.X, clip.Y), Vector2.Zero, "Shape projection center");
        var left = view.VirtualToScreenRect(0, 0, 31.3f, 10);
        var right = view.VirtualToScreenRect(31.3f, 0, 10, 10);
        Require(left.Right == right.Left, "Snapped adjacent rectangles leave a seam");

        int events = 0;
        view.RenderScaleChanged += scale =>
        {
            events++;
            Require(view.ScaledWidth == (int)(960 * scale) && view.ScaledHeight == view.ScaledWidth, "Scale event preceded metrics update");
        };
        view.RenderScale = 0.5f;
        Equal(view.WorldToScreen(center), new Vector2(480), "Render scale changed input coordinates");
        Require(view.ScaledHigherPowerOfTwo == 512, "Scaled allocation metrics");
        view.RenderScale = 0.1f;
        view.RenderScale = 0.1f;
        Require(events == 2 && view.RenderScale == 0.25f, "Clamped scale emitted a redundant event");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Reject(() => view.RenderScale = invalid);
        Require(events == 2 && view.RenderScale == 0.25f, "Invalid scale mutated state");
        Reject(() => view.Resize(0, 100));
        Reject(() => view.Resize(int.MaxValue, 100));
        Reject(() => view.FitWorld(Vector2.Zero, new Vector2(float.NaN, 10)));
        Require(view.ScreenWidth == 960 && view.ScreenHeight == 960, "Rejected resize changed metrics");
        view.Resize(1, 1);
        Require(view.ScaledWidth == 1 && view.ScaledHeight == 1, "Subpixel target dimensions");
        var large = new RenderView(1 << 30, 1);
        Require(large.ScreenHigherPowerOfTwo == 1 << 30 && large.ScreenLowerPowerOfTwo == 1 << 30, "Power-of-two boundary overflow");
        Console.WriteLine("PASS: portable view fitting, pointer/sprite/shape agreement, resize, resolution scaling, event order and invalid dimensions.");
    }
    private static void Equal(Vector2 actual, Vector2 expected, string message) => Require(Vector2.Distance(actual, expected) < 0.001f, message);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); throw new Exception("Invalid view input accepted"); }
        catch (ArgumentOutOfRangeException) { }
    }
}
