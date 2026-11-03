using Microsoft.Xna.Framework;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

[ComponentDescription("Perspective camera. World units, radians, right-handed coordinates with Y up.")]
public struct Camera3D : Component
{
    public Vector3 Position;
    public Vector3 Target;
    public Vector3 Up;
    public float FieldOfView;
    public float NearPlane;
    public float FarPlane;

    public Camera3D()
    {
        Position = new Vector3(7, 5, 9);
        Target = Vector3.Zero;
        Up = Vector3.Up;
        FieldOfView = MathHelper.PiOver4;
        NearPlane = 0.1f;
        FarPlane = 100f;
    }

    public readonly Matrix View => Matrix.CreateLookAt(Position, Target, Up);
    /// <summary>An optional entity transform supplies camera pose; scale never changes the lens.</summary>
    public readonly Camera3D WithTransform(in Transform3D transform)
    {
        var rotation = Matrix.CreateFromYawPitchRoll(transform.Rotation.Y, transform.Rotation.X, transform.Rotation.Z);
        var camera = this;
        camera.Position = transform.Position;
        camera.Target = camera.Position + Vector3.TransformNormal(Vector3.Forward, rotation);
        camera.Up = Vector3.TransformNormal(Vector3.Up, rotation);
        return camera;
    }
    public readonly Matrix Projection(float aspectRatio) =>
        Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);
}
