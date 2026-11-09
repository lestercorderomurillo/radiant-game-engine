using System;

namespace com.radiant.engine.graphics;

public enum Blend { One, Zero, SourceColor, InverseSourceColor, SourceAlpha, InverseSourceAlpha, DestinationColor, InverseDestinationColor, DestinationAlpha, InverseDestinationAlpha, BlendFactor, InverseBlendFactor, SourceAlphaSaturation }
public enum BlendFunction { Add, Subtract, ReverseSubtract, Min, Max }
public enum TextureFilter { Linear, Point, Anisotropic, LinearMipPoint, PointMipLinear, MinLinearMagPointMipLinear, MinLinearMagPointMipPoint, MinPointMagLinearMipLinear, MinPointMagLinearMipPoint }
public enum TextureAddressMode { Wrap, Clamp, Mirror, Border }
public enum CullMode { None, CullClockwiseFace, CullCounterClockwiseFace }
public enum FillMode { Solid, WireFrame }
public enum CompareFunction { Always, Never, Less, LessEqual, Equal, GreaterEqual, Greater, NotEqual }
public enum StencilOperation { Keep, Zero, Replace, Increment, Decrement, IncrementSaturation, DecrementSaturation, Invert }
public enum SpriteSortMode { Deferred, Immediate, Texture, BackToFront, FrontToBack }
[Flags]
public enum SpriteEffects { None = 0, FlipHorizontally = 1, FlipVertically = 2 }
[Flags]
public enum ColorWriteChannels { None = 0, Red = 1, Green = 2, Blue = 4, Alpha = 8, All = 15 }

/// <summary>Immutable, backend-independent BlendState description. Native resources belong to the renderer.</summary>
public sealed record BlendState
{
    public Blend ColorSourceBlend { get; init; } = Blend.One;
    public Blend ColorDestinationBlend { get; init; } = Blend.Zero;
    public BlendFunction ColorBlendFunction { get; init; } = BlendFunction.Add;
    public Blend AlphaSourceBlend { get; init; } = Blend.One;
    public Blend AlphaDestinationBlend { get; init; } = Blend.Zero;
    public BlendFunction AlphaBlendFunction { get; init; } = BlendFunction.Add;
    public ColorWriteChannels ColorWriteChannels { get; init; } = ColorWriteChannels.All;
    public ColorWriteChannels ColorWriteChannels1 { get; init; } = ColorWriteChannels.All;
    public ColorWriteChannels ColorWriteChannels2 { get; init; } = ColorWriteChannels.All;
    public ColorWriteChannels ColorWriteChannels3 { get; init; } = ColorWriteChannels.All;
    public Color BlendFactor { get; init; } = Color.White;
    public int MultiSampleMask { get; init; } = int.MaxValue;
    public static BlendState Opaque { get; } = new();
    public static BlendState AlphaBlend { get; } = new() { ColorDestinationBlend = Blend.InverseSourceAlpha, AlphaDestinationBlend = Blend.InverseSourceAlpha };
    public static BlendState Additive { get; } = new() { ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One, AlphaSourceBlend = Blend.SourceAlpha, AlphaDestinationBlend = Blend.One };
    public static BlendState NonPremultiplied { get; } = new() { ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha, AlphaSourceBlend = Blend.SourceAlpha, AlphaDestinationBlend = Blend.InverseSourceAlpha };
}

/// <summary>Immutable, backend-independent SamplerState description. Native resources belong to the renderer.</summary>
public sealed record SamplerState
{
    public TextureFilter Filter { get; init; } = TextureFilter.Linear;
    public TextureAddressMode AddressU { get; init; } = TextureAddressMode.Wrap;
    public TextureAddressMode AddressV { get; init; } = TextureAddressMode.Wrap;
    public TextureAddressMode AddressW { get; init; } = TextureAddressMode.Wrap;
    public int MaxAnisotropy { get; init; } = 4;
    public int MaxMipLevel { get; init; } = 0;
    public float MipMapLevelOfDetailBias { get; init; } = 0;
    public Color BorderColor { get; init; } = Color.White;
    public static SamplerState LinearClamp { get; } = new() { Filter = TextureFilter.Linear, AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp };
    public static SamplerState LinearWrap { get; } = new() { Filter = TextureFilter.Linear, AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap };
    public static SamplerState PointClamp { get; } = new() { Filter = TextureFilter.Point, AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp };
    public static SamplerState PointWrap { get; } = new() { Filter = TextureFilter.Point, AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap };
    public static SamplerState AnisotropicClamp { get; } = new() { Filter = TextureFilter.Anisotropic, AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp };
    public static SamplerState AnisotropicWrap { get; } = new() { Filter = TextureFilter.Anisotropic, AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap };
}

/// <summary>Immutable, backend-independent RasterizerState description. Native resources belong to the renderer.</summary>
public sealed record RasterizerState
{
    public CullMode CullMode { get; init; } = CullMode.CullCounterClockwiseFace;
    public FillMode FillMode { get; init; } = FillMode.Solid;
    public float DepthBias { get; init; } = 0;
    public float SlopeScaleDepthBias { get; init; } = 0;
    public bool MultiSampleAntiAlias { get; init; } = true;
    public bool ScissorTestEnable { get; init; } = false;
    public bool DepthClipEnable { get; init; } = true;
    public static RasterizerState CullNone { get; } = new() { CullMode = CullMode.None };
    public static RasterizerState CullClockwise { get; } = new() { CullMode = CullMode.CullClockwiseFace };
    public static RasterizerState CullCounterClockwise { get; } = new() { CullMode = CullMode.CullCounterClockwiseFace };
}

/// <summary>Immutable, backend-independent DepthStencilState description. Native resources belong to the renderer.</summary>
public sealed record DepthStencilState
{
    public bool DepthBufferEnable { get; init; } = true;
    public bool DepthBufferWriteEnable { get; init; } = true;
    public CompareFunction DepthBufferFunction { get; init; } = CompareFunction.LessEqual;
    public bool StencilEnable { get; init; } = false;
    public bool TwoSidedStencilMode { get; init; } = false;
    public int ReferenceStencil { get; init; } = 0;
    public int StencilMask { get; init; } = int.MaxValue;
    public int StencilWriteMask { get; init; } = int.MaxValue;
    public CompareFunction StencilFunction { get; init; } = CompareFunction.Always;
    public StencilOperation StencilPass { get; init; } = StencilOperation.Keep;
    public StencilOperation StencilFail { get; init; } = StencilOperation.Keep;
    public StencilOperation StencilDepthBufferFail { get; init; } = StencilOperation.Keep;
    public CompareFunction CounterClockwiseStencilFunction { get; init; } = CompareFunction.Always;
    public StencilOperation CounterClockwiseStencilPass { get; init; } = StencilOperation.Keep;
    public StencilOperation CounterClockwiseStencilFail { get; init; } = StencilOperation.Keep;
    public StencilOperation CounterClockwiseStencilDepthBufferFail { get; init; } = StencilOperation.Keep;
    public static DepthStencilState Default { get; } = new();
    public static DepthStencilState None { get; } = new() { DepthBufferEnable = false, DepthBufferWriteEnable = false };
    public static DepthStencilState DepthRead { get; } = new() { DepthBufferWriteEnable = false };
}

