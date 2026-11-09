using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

/// <summary>Median-split, preorder BVH with escape links: stackless GPU traversal, no fixed triangle limit.</summary>
public sealed class TraceBvh
{
    /// <summary>Bounding volume and child or triangle range in the tracing hierarchy.</summary>
    public readonly record struct Node(Vector3 Min, Vector3 Max, int Start, int Count, int Escape);
    public TraceTriangle[] Triangles { get; }
    public IReadOnlyList<Node> Nodes => Tree;

    private readonly List<Node> Tree = new();

    /// <summary>Builds a hierarchy from the supplied triangles for later shader packing.</summary>
    public TraceBvh(IReadOnlyList<TraceTriangle> triangles)
    {
        Triangles = new TraceTriangle[triangles.Count];

        for (int i = 0; i < triangles.Count; i++) Triangles[i] = triangles[i];

        if (Triangles.Length > 0) Build(0, Triangles.Length);
    }

    /// <summary>Constructs the hierarchy over the supplied trace triangles.</summary>
    private int Build(int start, int count)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        for (int i = start; i < start + count; i++)
        {
            var t = Triangles[i];
            min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
            max = Vector3.Max(max, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
        }

        int index = Tree.Count;
        Tree.Add(default);

        if (count > 4)
        {
            var size = max - min;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            Array.Sort(Triangles, start, count, Comparer<TraceTriangle>.Create((a, b) => Coordinate(a, axis).CompareTo(Coordinate(b, axis))));

            int half = count / 2;
            Build(start, half);
            Build(start + half, count - half);
        }

        Tree[index] = new(min - new Vector3(1e-4f), max + new Vector3(1e-4f), start, count <= 4 ? count : 0, Tree.Count);

        return index;
    }

    /// <summary>Computes the triangle centroid's unscaled coordinate on the selected split axis.</summary>
    private static float Coordinate(TraceTriangle t, int axis) => axis == 0 ? t.A.X + t.B.X + t.C.X : axis == 1 ? t.A.Y + t.B.Y + t.C.Y : t.A.Z + t.B.Z + t.C.Z;

    /// <summary>Encodes BVH nodes and triangles into the texture layout expected by the shader.</summary>
    public Vector4[] Pack(out int triangleOffset)
    {
        triangleOffset = Tree.Count * 3;

        var data = new Vector4[triangleOffset + Triangles.Length * 5];

        for (int i = 0; i < Tree.Count; i++)
        {
            var n = Tree[i];
            data[i * 3] = new(n.Min, n.Start);
            data[i * 3 + 1] = new(n.Max, n.Escape);
            data[i * 3 + 2] = new(n.Count, 0, 0, 0);
        }

        for (int i = 0; i < Triangles.Length; i++)
        {
            int k = triangleOffset + i * 5;
            var t = Triangles[i];
            data[k] = new(t.A, 0);
            data[k + 1] = new(t.B - t.A, 0);
            data[k + 2] = new(t.C - t.A, 0);
            data[k + 3] = new(t.Albedo, 0);
            data[k + 4] = new(t.Emission, 0);
        }

        return data;
    }
}
