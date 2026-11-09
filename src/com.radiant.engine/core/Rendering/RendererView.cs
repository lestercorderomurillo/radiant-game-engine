namespace com.radiant.engine.core;

public partial class Renderer
{
    /// <summary>Transforms world-space sprites into presentation pixels.</summary>
    public Matrix WorldTransform => View.WorldTransform;

    /// <summary>Fits the world proportionally and updates the backend's shape projection.</summary>
    public void FitWorld(Vector2 Center, Vector2 Size)
    {
        View.FitWorld(Center, Size);
        ShapeBatch.ViewProjection = View.ViewProjection;
    }

    public bool TryGetWorldFit(out Vector2 Center, out Vector2 Size) => View.TryGetWorldFit(out Center, out Size);
}
