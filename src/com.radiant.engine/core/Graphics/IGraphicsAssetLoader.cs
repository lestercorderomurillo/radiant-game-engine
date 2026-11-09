namespace com.radiant.engine.graphics;

/// <summary>
/// Loads a caller-owned Radiant resource handle. Implementations may share underlying
/// content, but disposing one handle must not invalidate another consumer's handle.
/// Effect parameters must be independent between loads. Caching belongs to the caller.
/// </summary>
public interface IGraphicsAssetLoader
{
    Texture2D LoadTexture(string name);
    Effect LoadEffect(string name);
}
