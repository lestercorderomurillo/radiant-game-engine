using System;
using com.radiant.engine.core;

namespace com.radiant.engine.graphics;

/// <summary>Render-thread submission of triangle geometry to the currently bound target.</summary>
public interface IGeometryBackend : IDisposable
{
    /// <summary>Consumes complete colored triangles synchronously without retaining the array.</summary>
    void DrawTriangles(ColorVertex[] vertices, Effect shader, BlendState blend,
        DepthStencilState depth, RasterizerState rasterizer);

    /// <summary>Draws immutable indexed geometry. Implementations may cache uploads until disposal.</summary>
    void DrawMesh(Mesh mesh, Effect shader, BlendState blend,
        DepthStencilState depth, RasterizerState rasterizer);
}
