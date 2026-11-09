using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

/// <summary>Triangle positions and surface properties consumed by ray tracing.</summary>
public readonly record struct TraceTriangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Albedo, Vector3 Emission);
/// <summary>View and projection data used to generate rays and track camera changes.</summary>
public readonly record struct TraceCamera(Matrix View, Matrix Projection, Vector3 Position, bool Orthographic, float FarPlane)
{
    public Matrix InverseViewProjection => MatrixMath.Invert(View * Projection);
}

/// <summary>Opt-in world geometry adapter. Ray tracing never requires per-entity 3D data in a 2D game.</summary>
public interface ITraceGeometry
{
    IReadOnlyList<TraceTriangle> Triangles { get; }
    long Revision { get; }
    TraceCamera Camera { get; }
    bool HasCamera { get; }
    Point Size { get; }
}
