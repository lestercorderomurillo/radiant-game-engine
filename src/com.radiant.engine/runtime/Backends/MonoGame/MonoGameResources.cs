using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using com.radiant.engine.graphics;
using Native = Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.runtime;

/// <summary>All native image access and format conversion stays inside the MonoGame backend.</summary>
internal static class MonoGameResources
{
    internal static Native.Texture2D NativeTexture(Texture2D texture, Native.GraphicsDevice device = null)
    {
        if (texture == null) return null;
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        var native = texture switch
        {
            MonoGameTexture image => image.Native,
            MonoGameRenderTarget target => target.Native,
            _ => throw new ArgumentException("Resource belongs to a different graphics backend.", nameof(texture))
        };
        if (device != null && native.GraphicsDevice != device)
            throw new ArgumentException("Resource belongs to a different graphics device.", nameof(texture));
        return native;
    }

    internal static Native.RenderTarget2D NativeTarget(RenderTarget2D target, Native.GraphicsDevice device = null) =>
        (Native.RenderTarget2D)NativeTexture(target, device);

    internal static Native.Effect NativeEffect(Effect effect, Native.GraphicsDevice device = null)
    {
        if (effect == null) return null;
        ObjectDisposedException.ThrowIf(effect.IsDisposed, effect);
        if (effect is not MonoGameEffect shader || (device != null && shader.Native.GraphicsDevice != device))
            throw new ArgumentException("Effect belongs to a different graphics backend or device.", nameof(effect));
        return shader.Native;
    }
    internal static Native.SurfaceFormat ToNative(SurfaceFormat value) => value switch
    {
        SurfaceFormat.Color => Native.SurfaceFormat.Color,
        SurfaceFormat.Alpha8 => Native.SurfaceFormat.Alpha8,
        SurfaceFormat.Single => Native.SurfaceFormat.Single,
        SurfaceFormat.Vector2 => Native.SurfaceFormat.Vector2,
        SurfaceFormat.Vector4 => Native.SurfaceFormat.Vector4,
        SurfaceFormat.HalfSingle => Native.SurfaceFormat.HalfSingle,
        SurfaceFormat.HalfVector2 => Native.SurfaceFormat.HalfVector2,
        SurfaceFormat.HalfVector4 => Native.SurfaceFormat.HalfVector4,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported graphics format.")
    };
    internal static SurfaceFormat FromNative(Native.SurfaceFormat value) => value switch
    {
        Native.SurfaceFormat.Color => SurfaceFormat.Color,
        Native.SurfaceFormat.Alpha8 => SurfaceFormat.Alpha8,
        Native.SurfaceFormat.Single => SurfaceFormat.Single,
        Native.SurfaceFormat.Vector2 => SurfaceFormat.Vector2,
        Native.SurfaceFormat.Vector4 => SurfaceFormat.Vector4,
        Native.SurfaceFormat.HalfSingle => SurfaceFormat.HalfSingle,
        Native.SurfaceFormat.HalfVector2 => SurfaceFormat.HalfVector2,
        Native.SurfaceFormat.HalfVector4 => SurfaceFormat.HalfVector4,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported graphics format.")
    };
    internal static Native.DepthFormat ToNative(DepthFormat value) => value switch
    {
        DepthFormat.None => Native.DepthFormat.None,
        DepthFormat.Depth16 => Native.DepthFormat.Depth16,
        DepthFormat.Depth24 => Native.DepthFormat.Depth24,
        DepthFormat.Depth24Stencil8 => Native.DepthFormat.Depth24Stencil8,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported graphics format.")
    };
    internal static DepthFormat FromNative(Native.DepthFormat value) => value switch
    {
        Native.DepthFormat.None => DepthFormat.None,
        Native.DepthFormat.Depth16 => DepthFormat.Depth16,
        Native.DepthFormat.Depth24 => DepthFormat.Depth24,
        Native.DepthFormat.Depth24Stencil8 => DepthFormat.Depth24Stencil8,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported graphics format.")
    };
    internal static Native.RenderTargetUsage ToNative(RenderTargetUsage value) => value switch
    {
        RenderTargetUsage.DiscardContents => Native.RenderTargetUsage.DiscardContents,
        RenderTargetUsage.PreserveContents => Native.RenderTargetUsage.PreserveContents,
        RenderTargetUsage.PlatformContents => Native.RenderTargetUsage.PlatformContents,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported graphics format.")
    };
    internal static RenderTargetUsage FromNative(Native.RenderTargetUsage value) => value switch
    {
        Native.RenderTargetUsage.DiscardContents => RenderTargetUsage.DiscardContents,
        Native.RenderTargetUsage.PreserveContents => RenderTargetUsage.PreserveContents,
        Native.RenderTargetUsage.PlatformContents => RenderTargetUsage.PlatformContents,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported graphics format.")
    };
}

internal sealed class MonoGameTexture : Texture2D
{
    internal Native.Texture2D Native { get; }
    private readonly bool OwnsNative;
    private bool Disposed;
    internal MonoGameTexture(Native.Texture2D native, bool ownsNative = true) { Native = native; OwnsNative = ownsNative; }
    public override int Width => Native.Width;
    public override int Height => Native.Height;
    public override int LevelCount => Native.LevelCount;
    public override SurfaceFormat Format => MonoGameResources.FromNative(Native.Format);
    public override bool IsDisposed => Disposed || Native.IsDisposed;
    public override void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        if (OwnsNative) Native.Dispose();
    }
    private void Check() => ObjectDisposedException.ThrowIf(IsDisposed, this);
    public override void GetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount)
    {
        Check();
        Native.GetData(level, rectangle.ToNative(), data, startIndex, elementCount);
    }
    public override void SetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount)
    {
        Check();
        Native.SetData(level, rectangle.ToNative(), data, startIndex, elementCount);
    }
    public override void SaveAsPng(Stream stream, int width, int height)
    {
        Check();
        Native.SaveAsPng(stream, width, height);
    }
}

internal sealed class MonoGameRenderTarget : RenderTarget2D
{
    internal Native.RenderTarget2D Native { get; }
    internal MonoGameRenderTarget(Native.RenderTarget2D native) => Native = native;
    public override int Width => Native.Width;
    public override int Height => Native.Height;
    public override int LevelCount => Native.LevelCount;
    public override SurfaceFormat Format => MonoGameResources.FromNative(Native.Format);
    public override bool IsDisposed => Native.IsDisposed;
    public override void Dispose() => Native.Dispose();
    private void Check() => ObjectDisposedException.ThrowIf(IsDisposed, this);
    public override void GetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount)
    {
        Check();
        Native.GetData(level, rectangle.ToNative(), data, startIndex, elementCount);
    }
    public override void SetData<T>(int level, Rectangle? rectangle, T[] data, int startIndex, int elementCount)
    {
        Check();
        Native.SetData(level, rectangle.ToNative(), data, startIndex, elementCount);
    }
    public override void SaveAsPng(Stream stream, int width, int height)
    {
        Check();
        Native.SaveAsPng(stream, width, height);
    }
    public override DepthFormat DepthStencilFormat => MonoGameResources.FromNative(Native.DepthStencilFormat);
    public override int MultiSampleCount => Native.MultiSampleCount;
    public override RenderTargetUsage RenderTargetUsage => MonoGameResources.FromNative(Native.RenderTargetUsage);
}

internal sealed class MonoGameEffect : Effect
{
    internal Native.Effect Native { get; }
    private readonly Dictionary<Native.EffectTechnique, Technique> NativeTechniques = new();
    public override bool IsDisposed => Native.IsDisposed;
    public override EffectParameters Parameters { get; }
    public override IReadOnlyDictionary<string, EffectTechnique> Techniques { get; }
    public override EffectTechnique CurrentTechnique
    {
        get { Check(); return NativeTechniques[Native.CurrentTechnique]; }
        set
        {
            Check();
            if (value is not Technique technique || technique.Owner != this)
                throw new ArgumentException("Technique belongs to another effect.", nameof(value));
            Native.CurrentTechnique = technique.Native;
        }
    }
    internal MonoGameEffect(Native.Effect native)
    {
        Native = native;
        Parameters = new ParametersCollection(this);
        var named = new Dictionary<string, EffectTechnique>();
        foreach (var technique in native.Techniques)
        {
            var wrapper = new Technique(this, technique);
            NativeTechniques.Add(technique, wrapper);
            named.Add(technique.Name, wrapper);
        }
        Techniques = new ReadOnlyDictionary<string, EffectTechnique>(named);
    }
    private void Check() => ObjectDisposedException.ThrowIf(IsDisposed, this);
    public override void Dispose() => Native.Dispose();

    private sealed class ParametersCollection : EffectParameters
    {
        private readonly MonoGameEffect Owner;
        private readonly Dictionary<string, EffectParameter> Values = new();
        internal ParametersCollection(MonoGameEffect owner)
        {
            Owner = owner;
            foreach (var parameter in owner.Native.Parameters) Values.Add(parameter.Name, new Parameter(owner, parameter));
        }
        public override EffectParameter this[string name]
        {
            get { Owner.Check(); return Values.TryGetValue(name, out var value) ? value : null; }
        }
    }
    private sealed class Technique : EffectTechnique
    {
        internal readonly MonoGameEffect Owner;
        internal readonly Native.EffectTechnique Native;
        public override string Name => Native.Name;
        public override IReadOnlyList<EffectPass> Passes { get; }
        internal Technique(MonoGameEffect owner, Native.EffectTechnique native)
        {
            Owner = owner;
            Native = native;
            Passes = Array.AsReadOnly(native.Passes.Select(pass => (EffectPass)new Pass(owner, pass)).ToArray());
        }
    }
    private sealed class Pass : EffectPass
    {
        private readonly MonoGameEffect Owner;
        private readonly Native.EffectPass Native;
        internal Pass(MonoGameEffect owner, Native.EffectPass native) { Owner = owner; Native = native; }
        public override void Apply() { Owner.Check(); Native.Apply(); }
    }
    private sealed class Parameter : EffectParameter
    {
        private readonly MonoGameEffect Owner;
        private readonly Native.EffectParameter Native;
        internal Parameter(MonoGameEffect owner, Native.EffectParameter native) { Owner = owner; Native = native; }
        public override void SetValue(float value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(int value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(bool value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(Vector2 value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(Vector3 value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(Vector4 value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(Matrix value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(Texture2D value) { Owner.Check(); Native.SetValue(MonoGameResources.NativeTexture(value, Owner.Native.GraphicsDevice)); }
        public override void SetValue(float[] value) { Owner.Check(); Native.SetValue(value); }
        public override void SetValue(Vector2[] value) { Owner.Check(); MonoGameMath.SetValue(Native, value); }
        public override void SetValue(Vector3[] value) { Owner.Check(); MonoGameMath.SetValue(Native, value); }
        public override void SetValue(Vector4[] value) { Owner.Check(); MonoGameMath.SetValue(Native, value); }
        public override void SetValue(Matrix[] value) { Owner.Check(); MonoGameMath.SetValue(Native, value); }
    }
}

internal sealed class MonoGameResourceFactory : IGraphicsResourceFactory
{
    private readonly Native.GraphicsDevice Device;
    internal MonoGameResourceFactory(Native.GraphicsDevice device) => Device = device;
    public Texture2D CreateTexture(int width, int height, SurfaceFormat format = SurfaceFormat.Color) =>
        new MonoGameTexture(new Native.Texture2D(Device, width, height, false, MonoGameResources.ToNative(format)));
    public RenderTarget2D CreateRenderTarget(int width, int height, SurfaceFormat format = SurfaceFormat.Color,
        DepthFormat depth = DepthFormat.None, RenderTargetUsage usage = RenderTargetUsage.DiscardContents, int samples = 0) =>
        new MonoGameRenderTarget(new Native.RenderTarget2D(Device, width, height, false,
            MonoGameResources.ToNative(format), MonoGameResources.ToNative(depth), samples, MonoGameResources.ToNative(usage)));
    public int GetSupportedSamples(int requested, SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None)
    {
        Device.Adapter.QueryRenderTargetFormat(Device.GraphicsProfile, MonoGameResources.ToNative(format),
            MonoGameResources.ToNative(depth), requested, out _, out _, out int selected);
        return selected;
    }
}
