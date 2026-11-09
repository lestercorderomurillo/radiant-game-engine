using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Zero-copy unlit output from any 2D or 3D geometry source.</summary>
[SystemTag("Rendering")]
public class FlatRenderer : ImageSystem
{
    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public override Texture2D GetOutput() => Input?.Invoke();
}
