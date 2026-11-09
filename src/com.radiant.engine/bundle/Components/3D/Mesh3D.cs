using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Opaque unlit mesh and base color. Geometry is shared; transforms belong to the entity.</summary>
[ComponentDescription("Opaque unlit mesh and base color. Geometry is shared; transforms belong to the entity.")]
public struct Mesh3D : Component
{
    public Mesh Geometry;
    public Color Color;
    /// <summary>Rasterize both sides of thin or open surfaces. Closed meshes cull back faces by default.</summary>
    public bool DoubleSided;

    /// <summary>Creates a mesh component with its default geometry and material settings.</summary>
    public Mesh3D()
    {
        Geometry = Mesh.Cube;
        Color = Color.White;
        DoubleSided = false;
    }
}
