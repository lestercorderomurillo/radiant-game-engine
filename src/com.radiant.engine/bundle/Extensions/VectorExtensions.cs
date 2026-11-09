using System;

namespace com.radiant.engine.bundle;

/// <summary>Component-wise vector arithmetic and mapping helpers.</summary>
public static class VectorExtensions
{
    /// <summary>Adds a scalar to each vector component.</summary>
    public static Vector3 Add(this Vector3 v, float scalar)
        => new Vector3(v.X + scalar, v.Y + scalar, v.Z + scalar);

    /// <summary>Subtracts a scalar from each vector component.</summary>
    public static Vector3 Subtract(this Vector3 v, float scalar)
        => new Vector3(v.X - scalar, v.Y - scalar, v.Z - scalar);

    /// <summary>Maps each vector component through the supplied function.</summary>
    public static Vector3 Apply(this Vector3 v, Func<float, float> func)
        => new Vector3(func(v.X), func(v.Y), func(v.Z));

    /// <summary>Combines corresponding components from two vectors through the supplied function.</summary>
    public static Vector3 Apply(this Vector3 v, Vector3 other, Func<float, float, float> func)
        => new Vector3(func(v.X, other.X), func(v.Y, other.Y), func(v.Z, other.Z));
}
