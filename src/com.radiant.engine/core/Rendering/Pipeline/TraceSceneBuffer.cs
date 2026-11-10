using System;

namespace com.radiant.engine.core;

/// <summary>Owns a GPU copy of an <see cref="ITraceGeometry"/> hierarchy for stages that bind this buffer.</summary>
/// <remarks>Call <see cref="Update"/> once per frame before binding; it rebuilds only when the source or its revision changes.</remarks>
public sealed class TraceSceneBuffer : IDisposable
{
    public Texture2D Data { get; private set; }
    public int NodeCount { get; private set; }
    public int TriangleCount { get; private set; }
    public int TriangleOffset { get; private set; }
    public int BuildCount { get; private set; }
    /// <summary>World bounds of the uploaded triangles; both are zero without geometry.</summary>
    public Vector3 BoundsMin { get; private set; }
    public Vector3 BoundsMax { get; private set; }
    public long UploadedRevision { get; private set; } = -1;
    public ITraceGeometry UploadedSource { get; private set; }

    /// <summary>Reports whether the uploaded data matches the geometry's current revision.</summary>
    public bool IsCurrent(ITraceGeometry geometry) => Data != null && ReferenceEquals(UploadedSource, geometry) && UploadedRevision == geometry.Revision;

    /// <summary>Rebuilds and uploads the hierarchy when needed; returns true when the GPU data changed.</summary>
    public bool Update(IRenderer renderer, ITraceGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(geometry);

        if (IsCurrent(geometry)) return false;

        var bvh = new TraceBvh(geometry.Triangles);
        var packed = bvh.Pack(out int triangleOffset);
        int width = Math.Min(1024, Math.Max(1, packed.Length));
        int height = Math.Max(1, (packed.Length + width - 1) / width);
        var pixels = new Vector4[width * height];
        Array.Copy(packed, pixels, packed.Length);
        Data?.Dispose();
        Data = renderer.CreateTexture(width, height, SurfaceFormat.Vector4);
        Data.SetData(pixels);
        TriangleOffset = triangleOffset;
        NodeCount = bvh.Nodes.Count;
        TriangleCount = bvh.Triangles.Length;
        BoundsMin = NodeCount > 0 ? bvh.Nodes[0].Min : Vector3.Zero;
        BoundsMax = NodeCount > 0 ? bvh.Nodes[0].Max : Vector3.Zero;
        BuildCount++;
        global::System.Diagnostics.Debug.WriteLine($"Trace BVH: {NodeCount} nodes, {TriangleCount} triangles, {width}x{height}");
        UploadedRevision = geometry.Revision;
        UploadedSource = geometry;

        return true;
    }

    /// <summary>Sets the scene parameters expected by shaders that include TraceScene.fxh.</summary>
    public IRenderer Bind(IRenderer renderer)
    {
        if (Data == null) throw new InvalidOperationException("Update the trace scene before binding it.");

        return renderer.SetParameter("SceneData", Data).SetParameter("DataWidth", Data.Width).SetParameter("DataSize", new Vector2(Data.Width, Data.Height))
            .SetParameter("TriangleOffset", TriangleOffset).SetParameter("NodeCount", NodeCount);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Data?.Dispose();
        Data = null;
        UploadedRevision = -1;
        UploadedSource = null;
        NodeCount = TriangleCount = TriangleOffset = 0;
        BoundsMin = BoundsMax = Vector3.Zero;
    }
}
