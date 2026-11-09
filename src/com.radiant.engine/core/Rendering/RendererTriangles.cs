using System;

namespace com.radiant.engine.core;

public partial class Renderer
{
    /// <summary>Submits complete colored triangles with the configured shader and render state.</summary>
    public IRenderer DrawTriangles(ColorVertex[] vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Length == 0) return this;
        if (vertices.Length % 3 != 0) throw new ArgumentException("Triangle vertices must be a multiple of three.", nameof(vertices));

        CommitTextures();
        Geometry.DrawTriangles(vertices, CurrentShader, BlendState, DepthStencilState, RasterizerState);
        return this;
    }
}
