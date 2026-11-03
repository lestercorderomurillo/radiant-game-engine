using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>Immutable indexed triangle mesh. GPU copies are owned by each Renderer.</summary>
public sealed class Mesh
{
    internal readonly VertexPositionNormalTexture[] Vertices;
    internal readonly int[] Indices;
    public int VertexCount => Vertices.Length;
    public int TriangleCount => Indices.Length / 3;

    public Mesh(VertexPositionNormalTexture[] vertices, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (vertices.Length < 3 || indices.Length == 0 || indices.Length % 3 != 0)
            throw new ArgumentException("A mesh requires vertices and complete indexed triangles.");
        foreach (int index in indices)
            if (index < 0 || index >= vertices.Length)
                throw new ArgumentOutOfRangeException(nameof(indices));
        Vertices = (VertexPositionNormalTexture[])vertices.Clone();
        Indices = (int[])indices.Clone();
    }

    public static Mesh Plane { get; } = new(new[]
    {
        new VertexPositionNormalTexture(new(-0.5f, 0, -0.5f), Vector3.Up, Vector2.Zero),
        new VertexPositionNormalTexture(new(-0.5f, 0, 0.5f), Vector3.Up, Vector2.UnitY),
        new VertexPositionNormalTexture(new(0.5f, 0, 0.5f), Vector3.Up, Vector2.One),
        new VertexPositionNormalTexture(new(0.5f, 0, -0.5f), Vector3.Up, Vector2.UnitX)
    }, new[] { 0, 1, 2, 0, 2, 3 });

    public static Mesh Cube { get; } = CreateCube();

    private static Mesh CreateCube()
    {
        var vertices = new VertexPositionNormalTexture[24];
        var indices = new int[36];
        Vector3[] normals = { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Backward, Vector3.Forward };
        for (int face = 0; face < normals.Length; face++)
        {
            var n = normals[face];
            var u = MathF.Abs(n.Y) > 0.5f ? Vector3.Right : Vector3.Normalize(Vector3.Cross(Vector3.Up, n));
            var v = Vector3.Cross(n, u);
            Vector3[] corners = { n - u - v, n + u - v, n + u + v, n - u + v };
            for (int i = 0; i < 4; i++)
                vertices[face * 4 + i] = new(corners[i] * 0.5f, n, Vector2.Zero);
            int b = face * 4, k = face * 6;
            indices[k] = b; indices[k + 1] = b + 1; indices[k + 2] = b + 2;
            indices[k + 3] = b; indices[k + 4] = b + 2; indices[k + 5] = b + 3;
        }
        return new Mesh(vertices, indices);
    }
}
