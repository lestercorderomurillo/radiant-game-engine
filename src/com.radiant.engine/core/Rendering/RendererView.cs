using System;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public partial class Renderer
{
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
        if (!float.IsFinite(Center.X) || !float.IsFinite(Center.Y))
            throw new ArgumentOutOfRangeException(nameof(Center));
        if (!float.IsFinite(Size.X) || !float.IsFinite(Size.Y) || Size.X <= 0 || Size.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(Size));
        FittedWorldCenter = Center;
        FittedWorldSize = Size;
        UpdateWorldView();
    }

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
        ShapeBatch.ViewProjection = WorldTransform
            * Matrix.CreateOrthographicOffCenter(0, ScreenWidth, ScreenHeight, 0, 0, 1);
    }
}
