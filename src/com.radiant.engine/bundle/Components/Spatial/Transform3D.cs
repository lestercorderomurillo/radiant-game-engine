using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Where a 3D entity is, how it is rotated (radians) and how it is scaled.</summary>
[ComponentDescription("3D position, Euler rotation in radians and scale.")]
public struct Transform3D : Component
{
    /// <summary>World position.</summary>
    public Vector3 Position;
    /// <summary>Rotation in radians around X (pitch), Y (yaw) and Z (roll).</summary>
    public Vector3 Rotation;
    /// <summary>Size multiplier per axis.</summary>
    public Vector3 Scale;

    /// <summary>Creates a transform at the origin with no rotation and normal scale.</summary>
    public Transform3D() { Position = Vector3.Zero; Rotation = Vector3.Zero; Scale = Vector3.One; }

    /// <summary>World matrix combining scale, rotation and position.</summary>
    public readonly Matrix Matrix => Matrix.CreateScale(Scale) * Matrix.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z) * Matrix.CreateTranslation(Position);
}
