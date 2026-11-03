using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>Geometry submission for lighting before sample resolve. The bound effect provides Meshes/Shapes techniques.</summary>
public interface IRasterGeometry
{
    Point Size { get; }
    int RequestedSamples { get; }
    DepthFormat VisibilityFormat { get; }
    void Draw(Renderer renderer);
}
