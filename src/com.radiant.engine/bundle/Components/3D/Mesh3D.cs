using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Draws a mesh with a base color at the entity's <c>Transform3D</c>. Many entities can share one mesh.</summary>
[ComponentDescription("Opaque unlit mesh and base color. Geometry is shared; transforms belong to the entity.")]
public struct Mesh3D : Component
{
    /// <summary>The shared mesh to draw.</summary>
    public Mesh Geometry;
    /// <summary>Base surface color.</summary>
    public Color Color;
    /// <summary>Rasterize both sides of thin or open surfaces. Closed meshes cull back faces by default.</summary>
    public bool DoubleSided;

    /// <summary>Creates an empty mesh component with a white color.</summary>
    public Mesh3D()
    {
        Geometry = Mesh.Cube;
        Color = Color.White;
        DoubleSided = false;
    }
}
