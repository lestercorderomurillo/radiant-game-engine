using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>A perspective camera that looks from <c>Position</c> toward <c>Target</c>. Units are world units and angles are radians; Y is up.</summary>
/// <example>
/// The following example shows how to place a camera looking at the origin:
/// <code>
/// int camera = ECS.CreateEntity();
/// ECS.AddComponent&lt;Camera3D&gt;(camera) = new Camera3D { Position = new Vector3(0, 2, 6), Target = Vector3.Zero };
/// </code>
/// </example>
[ComponentDescription("Perspective camera. World units, radians, right-handed coordinates with Y up.")]
public struct Camera3D : Component
{
    /// <summary>Where the camera is.</summary>
    public Vector3 Position;
    /// <summary>The point the camera looks at.</summary>
    public Vector3 Target;
    /// <summary>Which way is up for the camera, usually <c>Vector3.UnitY</c>.</summary>
    public Vector3 Up;
    /// <summary>Vertical field of view in radians.</summary>
    public float FieldOfView;
    /// <summary>Closest visible distance.</summary>
    public float NearPlane;
    /// <summary>Farthest visible distance.</summary>
    public float FarPlane;

    /// <summary>Creates a camera with a typical field of view and clip range.</summary>
    public Camera3D()
    {
        Position = new Vector3(7, 5, 9);
        Target = Vector3.Zero;
        Up = Vector3.UnitY;
        FieldOfView = (global::System.MathF.PI / 4);
        NearPlane = 0.1f;
        FarPlane = 100f;
    }

    /// <summary>View matrix for shaders.</summary>
    public readonly Matrix View => Matrix.CreateLookAt(Position, Target, Up);

    /// <summary>Returns this camera posed by an entity's transform, so the view can follow it. Scale is ignored.</summary>
    public readonly Camera3D WithTransform(in Transform3D transform)
    {
        var rotation = Matrix.CreateFromYawPitchRoll(transform.Rotation.Y, transform.Rotation.X, transform.Rotation.Z);
        var camera = this;
        camera.Position = transform.Position;
        camera.Target = camera.Position + Vector3.TransformNormal((-Vector3.UnitZ), rotation);
        camera.Up = Vector3.TransformNormal(Vector3.UnitY, rotation);

        return camera;
    }

    /// <summary>Projection matrix for a viewport aspect ratio, such as <c>Renderer.AspectRatio</c>.</summary>
    public readonly Matrix Projection(float aspectRatio) =>
        Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);
}
