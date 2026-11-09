using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional linear HDR radiance emitted by a surface.</summary>
[ComponentDescription("Optional linear HDR radiance emitted by a surface.")]
public struct SurfaceEmission : Component
{
    public Vector3 Radiance;
}
