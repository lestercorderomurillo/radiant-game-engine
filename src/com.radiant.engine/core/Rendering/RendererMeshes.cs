using System;

namespace com.radiant.engine.core;

public partial class Renderer
{
    /// <summary>Draws an indexed mesh with the configured shader and render state.</summary>
    public IRenderer DrawMesh(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        CommitTextures();
        Geometry.DrawMesh(mesh, CurrentShader, BlendState, DepthStencilState, RasterizerState);
        return this;
    }
}
