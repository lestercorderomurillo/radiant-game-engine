using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>3D position, Euler rotation in radians and scale.</summary>
[ComponentDescription("3D position, Euler rotation in radians and scale.")]
public struct Transform3D : Component
{
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale;

    /// <summary>Creates a 3D transform with default position, rotation, and scale.</summary>
    public Transform3D() { Position = Vector3.Zero; Rotation = Vector3.Zero; Scale = Vector3.One; }

    public readonly Matrix Matrix => Matrix.CreateScale(Scale) * Matrix.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z) * Matrix.CreateTranslation(Position);
}
