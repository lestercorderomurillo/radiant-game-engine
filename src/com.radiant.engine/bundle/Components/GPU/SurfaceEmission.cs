using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

[ComponentDescription("Optional linear HDR radiance emitted by a surface.")]
public struct SurfaceEmission : Component
{
    public Vector3 Radiance;
}
