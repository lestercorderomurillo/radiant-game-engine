using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Makes a 3D surface glow with HDR brightness for the path tracer. Values above 1 are brighter than white.</summary>
[ComponentDescription("Optional linear HDR radiance emitted by a surface.")]
public struct SurfaceEmission : Component
{
    /// <summary>Emitted light per color channel, in linear units.</summary>
    public Vector3 Radiance;
}
