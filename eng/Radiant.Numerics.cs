// Internal source aliases. Public signatures contain System.Numerics and Radiant types.
// Keep native math and primitive conversions at the backend boundary.
global using Vector2 = global::System.Numerics.Vector2;
global using Vector3 = global::System.Numerics.Vector3;
global using Vector4 = global::System.Numerics.Vector4;
global using Matrix = global::System.Numerics.Matrix4x4;
global using com.radiant.engine.math;
global using Color = global::com.radiant.engine.math.Color;
global using Point = global::com.radiant.engine.math.Point;
global using Rectangle = global::com.radiant.engine.math.Rectangle;
global using Blend = global::com.radiant.engine.graphics.Blend;
global using BlendFunction = global::com.radiant.engine.graphics.BlendFunction;
global using TextureFilter = global::com.radiant.engine.graphics.TextureFilter;
global using TextureAddressMode = global::com.radiant.engine.graphics.TextureAddressMode;
global using CullMode = global::com.radiant.engine.graphics.CullMode;
global using FillMode = global::com.radiant.engine.graphics.FillMode;
global using CompareFunction = global::com.radiant.engine.graphics.CompareFunction;
global using StencilOperation = global::com.radiant.engine.graphics.StencilOperation;
global using SpriteSortMode = global::com.radiant.engine.graphics.SpriteSortMode;
global using SpriteEffects = global::com.radiant.engine.graphics.SpriteEffects;
global using ColorWriteChannels = global::com.radiant.engine.graphics.ColorWriteChannels;
global using BlendState = global::com.radiant.engine.graphics.BlendState;
global using SamplerState = global::com.radiant.engine.graphics.SamplerState;
global using RasterizerState = global::com.radiant.engine.graphics.RasterizerState;
global using DepthStencilState = global::com.radiant.engine.graphics.DepthStencilState;
