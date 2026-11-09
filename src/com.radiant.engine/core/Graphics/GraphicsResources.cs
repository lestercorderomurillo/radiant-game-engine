using System;
using System.IO;
using System.Collections.Generic;

namespace com.radiant.engine.graphics;

/// <summary>Portable, uncompressed pixel layouts supported by Radiant.</summary>
public enum SurfaceFormat { Color, Alpha8, Single, Vector2, Vector4, HalfSingle, HalfVector2, HalfVector4 }
public enum DepthFormat { None, Depth16, Depth24, Depth24Stencil8 }
public enum RenderTargetUsage { DiscardContents, PreserveContents, PlatformContents }

/// <summary>A backend-owned resource. Dispose caller-created resources before their renderer.</summary>
public interface IGraphicsResource : IDisposable
{
    bool IsDisposed { get; }
}

/// <summary>Factory contract for graphics backends; all signatures use Radiant types.</summary>
public interface IGraphicsResourceFactory
{
    Texture2D CreateTexture(int width, int height, SurfaceFormat format = SurfaceFormat.Color);
    RenderTarget2D CreateRenderTarget(int width, int height, SurfaceFormat format = SurfaceFormat.Color,
        DepthFormat depth = DepthFormat.None, RenderTargetUsage usage = RenderTargetUsage.DiscardContents, int samples = 0);
    int GetSupportedSamples(int requested, SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None);
}

/// <summary>A 2D image contract implementable by any graphics backend. Create through Renderer.</summary>
public abstract class Texture2D : IGraphicsResource
{
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract int LevelCount { get; }
    public abstract SurfaceFormat Format { get; }
    public Rectangle Bounds => new(0, 0, Width, Height);
    public abstract bool IsDisposed { get; }
    public abstract void Dispose();
    public void GetData<T>(T[] data) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        GetData(0, null, data, 0, data.Length);
    }
    public void GetData<T>(T[] data, int startIndex, int elementCount) where T : struct => GetData(0, null, data, startIndex, elementCount);
    public abstract void GetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount) where T : struct;
    public void SetData<T>(T[] data) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        SetData(0, null, data, 0, data.Length);
    }
    public void SetData<T>(T[] data, int startIndex, int elementCount) where T : struct => SetData(0, null, data, startIndex, elementCount);
    public abstract void SetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount) where T : struct;
    public abstract void SaveAsPng(Stream stream, int width, int height);
}

/// <summary>A drawable image with backend-selected depth and sample configuration.</summary>
public abstract class RenderTarget2D : Texture2D
{
    public abstract DepthFormat DepthStencilFormat { get; }
    public abstract int MultiSampleCount { get; }
    public abstract RenderTargetUsage RenderTargetUsage { get; }
}

/// <summary>A compiled shader owned by the renderer cache. Release through Renderer.ReleaseShader.</summary>
public abstract class Effect : IGraphicsResource
{
    public abstract bool IsDisposed { get; }
    public abstract EffectParameters Parameters { get; }
    public abstract IReadOnlyDictionary<string, EffectTechnique> Techniques { get; }
    public abstract EffectTechnique CurrentTechnique { get; set; }
    public abstract void Dispose();
}

/// <summary>Named parameters; an absent optional parameter returns null.</summary>
public abstract class EffectParameters
{
    public abstract EffectParameter this[string name] { get; }
}

public abstract class EffectTechnique
{
    public abstract string Name { get; }
    public abstract IReadOnlyList<EffectPass> Passes { get; }
}

public abstract class EffectPass
{
    public abstract void Apply();
}

/// <summary>Typed shader inputs. Array conversions and storage belong to the backend.</summary>
public abstract class EffectParameter
{
    public abstract void SetValue(float value);
    public abstract void SetValue(int value);
    public abstract void SetValue(bool value);
    public abstract void SetValue(Vector2 value);
    public abstract void SetValue(Vector3 value);
    public abstract void SetValue(Vector4 value);
    public abstract void SetValue(Matrix value);
    public abstract void SetValue(Texture2D value);
    public abstract void SetValue(float[] value);
    public abstract void SetValue(Vector2[] value);
    public abstract void SetValue(Vector3[] value);
    public abstract void SetValue(Vector4[] value);
    public abstract void SetValue(Matrix[] value);
}
