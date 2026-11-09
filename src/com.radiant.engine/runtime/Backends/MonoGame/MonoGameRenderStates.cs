using System;
using System.Collections.Generic;
using Native = Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.runtime;

// A cache belongs to one renderer/device lifetime. Equal descriptions reuse native resources.
internal sealed class MonoGameRenderStates : IDisposable
{
    private readonly Dictionary<BlendState, Native.BlendState> BlendStateCache = new();
    internal Native.BlendState Get(BlendState state)
    {
        if (state is null) return null;
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (BlendStateCache.TryGetValue(state, out var cached)) return cached;
        var native = new Native.BlendState
        {
            ColorSourceBlend = ToNative(state.ColorSourceBlend),
            ColorDestinationBlend = ToNative(state.ColorDestinationBlend),
            ColorBlendFunction = ToNative(state.ColorBlendFunction),
            AlphaSourceBlend = ToNative(state.AlphaSourceBlend),
            AlphaDestinationBlend = ToNative(state.AlphaDestinationBlend),
            AlphaBlendFunction = ToNative(state.AlphaBlendFunction),
            ColorWriteChannels = ToNative(state.ColorWriteChannels),
            ColorWriteChannels1 = ToNative(state.ColorWriteChannels1),
            ColorWriteChannels2 = ToNative(state.ColorWriteChannels2),
            ColorWriteChannels3 = ToNative(state.ColorWriteChannels3),
            BlendFactor = state.BlendFactor.ToNative(),
            MultiSampleMask = state.MultiSampleMask,
        };
        BlendStateCache.Add(state, native);
        return native;
    }
    private readonly Dictionary<SamplerState, Native.SamplerState> SamplerStateCache = new();
    internal Native.SamplerState Get(SamplerState state)
    {
        if (state is null) return null;
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (SamplerStateCache.TryGetValue(state, out var cached)) return cached;
        var native = new Native.SamplerState
        {
            Filter = ToNative(state.Filter),
            AddressU = ToNative(state.AddressU),
            AddressV = ToNative(state.AddressV),
            AddressW = ToNative(state.AddressW),
            MaxAnisotropy = state.MaxAnisotropy,
            MaxMipLevel = state.MaxMipLevel,
            MipMapLevelOfDetailBias = state.MipMapLevelOfDetailBias,
            BorderColor = state.BorderColor.ToNative(),
        };
        SamplerStateCache.Add(state, native);
        return native;
    }
    private readonly Dictionary<RasterizerState, Native.RasterizerState> RasterizerStateCache = new();
    internal Native.RasterizerState Get(RasterizerState state)
    {
        if (state is null) return null;
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (RasterizerStateCache.TryGetValue(state, out var cached)) return cached;
        var native = new Native.RasterizerState
        {
            CullMode = ToNative(state.CullMode),
            FillMode = ToNative(state.FillMode),
            DepthBias = state.DepthBias,
            SlopeScaleDepthBias = state.SlopeScaleDepthBias,
            MultiSampleAntiAlias = state.MultiSampleAntiAlias,
            ScissorTestEnable = state.ScissorTestEnable,
            DepthClipEnable = state.DepthClipEnable,
        };
        RasterizerStateCache.Add(state, native);
        return native;
    }
    private readonly Dictionary<DepthStencilState, Native.DepthStencilState> DepthStencilStateCache = new();
    internal Native.DepthStencilState Get(DepthStencilState state)
    {
        if (state is null) return null;
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (DepthStencilStateCache.TryGetValue(state, out var cached)) return cached;
        var native = new Native.DepthStencilState
        {
            DepthBufferEnable = state.DepthBufferEnable,
            DepthBufferWriteEnable = state.DepthBufferWriteEnable,
            DepthBufferFunction = ToNative(state.DepthBufferFunction),
            StencilEnable = state.StencilEnable,
            TwoSidedStencilMode = state.TwoSidedStencilMode,
            ReferenceStencil = state.ReferenceStencil,
            StencilMask = state.StencilMask,
            StencilWriteMask = state.StencilWriteMask,
            StencilFunction = ToNative(state.StencilFunction),
            StencilPass = ToNative(state.StencilPass),
            StencilFail = ToNative(state.StencilFail),
            StencilDepthBufferFail = ToNative(state.StencilDepthBufferFail),
            CounterClockwiseStencilFunction = ToNative(state.CounterClockwiseStencilFunction),
            CounterClockwiseStencilPass = ToNative(state.CounterClockwiseStencilPass),
            CounterClockwiseStencilFail = ToNative(state.CounterClockwiseStencilFail),
            CounterClockwiseStencilDepthBufferFail = ToNative(state.CounterClockwiseStencilDepthBufferFail),
        };
        DepthStencilStateCache.Add(state, native);
        return native;
    }
    internal static Native.Blend ToNative(Blend value) => value switch
    {
        Blend.One => Native.Blend.One,
        Blend.Zero => Native.Blend.Zero,
        Blend.SourceColor => Native.Blend.SourceColor,
        Blend.InverseSourceColor => Native.Blend.InverseSourceColor,
        Blend.SourceAlpha => Native.Blend.SourceAlpha,
        Blend.InverseSourceAlpha => Native.Blend.InverseSourceAlpha,
        Blend.DestinationColor => Native.Blend.DestinationColor,
        Blend.InverseDestinationColor => Native.Blend.InverseDestinationColor,
        Blend.DestinationAlpha => Native.Blend.DestinationAlpha,
        Blend.InverseDestinationAlpha => Native.Blend.InverseDestinationAlpha,
        Blend.BlendFactor => Native.Blend.BlendFactor,
        Blend.InverseBlendFactor => Native.Blend.InverseBlendFactor,
        Blend.SourceAlphaSaturation => Native.Blend.SourceAlphaSaturation,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.BlendFunction ToNative(BlendFunction value) => value switch
    {
        BlendFunction.Add => Native.BlendFunction.Add,
        BlendFunction.Subtract => Native.BlendFunction.Subtract,
        BlendFunction.ReverseSubtract => Native.BlendFunction.ReverseSubtract,
        BlendFunction.Min => Native.BlendFunction.Min,
        BlendFunction.Max => Native.BlendFunction.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.TextureFilter ToNative(TextureFilter value) => value switch
    {
        TextureFilter.Linear => Native.TextureFilter.Linear,
        TextureFilter.Point => Native.TextureFilter.Point,
        TextureFilter.Anisotropic => Native.TextureFilter.Anisotropic,
        TextureFilter.LinearMipPoint => Native.TextureFilter.LinearMipPoint,
        TextureFilter.PointMipLinear => Native.TextureFilter.PointMipLinear,
        TextureFilter.MinLinearMagPointMipLinear => Native.TextureFilter.MinLinearMagPointMipLinear,
        TextureFilter.MinLinearMagPointMipPoint => Native.TextureFilter.MinLinearMagPointMipPoint,
        TextureFilter.MinPointMagLinearMipLinear => Native.TextureFilter.MinPointMagLinearMipLinear,
        TextureFilter.MinPointMagLinearMipPoint => Native.TextureFilter.MinPointMagLinearMipPoint,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.TextureAddressMode ToNative(TextureAddressMode value) => value switch
    {
        TextureAddressMode.Wrap => Native.TextureAddressMode.Wrap,
        TextureAddressMode.Clamp => Native.TextureAddressMode.Clamp,
        TextureAddressMode.Mirror => Native.TextureAddressMode.Mirror,
        TextureAddressMode.Border => Native.TextureAddressMode.Border,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.CullMode ToNative(CullMode value) => value switch
    {
        CullMode.None => Native.CullMode.None,
        CullMode.CullClockwiseFace => Native.CullMode.CullClockwiseFace,
        CullMode.CullCounterClockwiseFace => Native.CullMode.CullCounterClockwiseFace,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.FillMode ToNative(FillMode value) => value switch
    {
        FillMode.Solid => Native.FillMode.Solid,
        FillMode.WireFrame => Native.FillMode.WireFrame,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.CompareFunction ToNative(CompareFunction value) => value switch
    {
        CompareFunction.Always => Native.CompareFunction.Always,
        CompareFunction.Never => Native.CompareFunction.Never,
        CompareFunction.Less => Native.CompareFunction.Less,
        CompareFunction.LessEqual => Native.CompareFunction.LessEqual,
        CompareFunction.Equal => Native.CompareFunction.Equal,
        CompareFunction.GreaterEqual => Native.CompareFunction.GreaterEqual,
        CompareFunction.Greater => Native.CompareFunction.Greater,
        CompareFunction.NotEqual => Native.CompareFunction.NotEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.StencilOperation ToNative(StencilOperation value) => value switch
    {
        StencilOperation.Keep => Native.StencilOperation.Keep,
        StencilOperation.Zero => Native.StencilOperation.Zero,
        StencilOperation.Replace => Native.StencilOperation.Replace,
        StencilOperation.Increment => Native.StencilOperation.Increment,
        StencilOperation.Decrement => Native.StencilOperation.Decrement,
        StencilOperation.IncrementSaturation => Native.StencilOperation.IncrementSaturation,
        StencilOperation.DecrementSaturation => Native.StencilOperation.DecrementSaturation,
        StencilOperation.Invert => Native.StencilOperation.Invert,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.SpriteSortMode ToNative(SpriteSortMode value) => value switch
    {
        SpriteSortMode.Deferred => Native.SpriteSortMode.Deferred,
        SpriteSortMode.Immediate => Native.SpriteSortMode.Immediate,
        SpriteSortMode.Texture => Native.SpriteSortMode.Texture,
        SpriteSortMode.BackToFront => Native.SpriteSortMode.BackToFront,
        SpriteSortMode.FrontToBack => Native.SpriteSortMode.FrontToBack,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported render state value.")
    };
    internal static Native.SpriteEffects ToNative(SpriteEffects value)
    {
        if (((int)value & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(value));
        Native.SpriteEffects result = 0;
        if ((value & SpriteEffects.FlipHorizontally) != 0) result |= Native.SpriteEffects.FlipHorizontally;
        if ((value & SpriteEffects.FlipVertically) != 0) result |= Native.SpriteEffects.FlipVertically;
        return result;
    }
    internal static Native.ColorWriteChannels ToNative(ColorWriteChannels value)
    {
        if (((int)value & ~15) != 0) throw new ArgumentOutOfRangeException(nameof(value));
        Native.ColorWriteChannels result = 0;
        if ((value & ColorWriteChannels.Red) != 0) result |= Native.ColorWriteChannels.Red;
        if ((value & ColorWriteChannels.Green) != 0) result |= Native.ColorWriteChannels.Green;
        if ((value & ColorWriteChannels.Blue) != 0) result |= Native.ColorWriteChannels.Blue;
        if ((value & ColorWriteChannels.Alpha) != 0) result |= Native.ColorWriteChannels.Alpha;
        return result;
    }
    private bool Disposed;
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        foreach (var state in BlendStateCache.Values) state.Dispose();
        BlendStateCache.Clear();
        foreach (var state in SamplerStateCache.Values) state.Dispose();
        SamplerStateCache.Clear();
        foreach (var state in RasterizerStateCache.Values) state.Dispose();
        RasterizerStateCache.Clear();
        foreach (var state in DepthStencilStateCache.Values) state.Dispose();
        DepthStencilStateCache.Clear();
    }
}
