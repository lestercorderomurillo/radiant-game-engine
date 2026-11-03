using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

[ComponentDescription("3D position, Euler rotation in radians and scale.")]
public struct Transform3D : Component
{
    public Vector3 Position;
    public Vector3 Rotation;
    public Vector3 Scale;

    public Transform3D() { Position = Vector3.Zero; Rotation = Vector3.Zero; Scale = Vector3.One; }
    public readonly Matrix Matrix => Matrix.CreateScale(Scale) * Matrix.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z) * Matrix.CreateTranslation(Position);
}
