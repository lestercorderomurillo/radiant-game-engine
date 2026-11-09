using System;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.graphics;

namespace com.radiant.engine.runtime;

internal sealed class MonoGameGraphicsCommands : IGraphicsCommands
{
    private readonly GraphicsDevice Device;
    private readonly SpriteBatch Sprites;
    private readonly MonoGameRenderStates States = new();
    private readonly VertexBuffer Quad;
    private readonly IndexBuffer Indices;
    private bool Disposed;
    public Rectangle ViewportBounds => Device.Viewport.Bounds.ToRadiant();

    internal MonoGameGraphicsCommands(GraphicsDevice device, SpriteBatch sprites)
    {
        Device = device;
        Sprites = sprites;
        Quad = new VertexBuffer(device, typeof(VertexPositionTexture), 4, BufferUsage.WriteOnly);
        try
        {
            Quad.SetData(new VertexPositionTexture[]
            {
                new(new Vector3(-1, 1, 0), new Vector2(0, 0)),
                new(new Vector3(1, 1, 0), new Vector2(1, 0)),
                new(new Vector3(-1, -1, 0), new Vector2(0, 1)),
                new(new Vector3(1, -1, 0), new Vector2(1, 1))
            });
            Indices = new IndexBuffer(device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
            Indices.SetData(new short[] { 0, 1, 2, 2, 1, 3 });
        }
        catch { Quad.Dispose(); Indices?.Dispose(); throw; }
    }

    public void SetTargets(ReadOnlySpan<RenderTarget2D> targets)
    {
        Check();
        if (targets.Length == 0) { Device.SetRenderTarget(null); return; }
        if (targets.Length == 1) { Device.SetRenderTarget(MonoGameResources.NativeTarget(targets[0], Device)); return; }
        var bindings = new RenderTargetBinding[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            bindings[i] = new RenderTargetBinding(MonoGameResources.NativeTarget(targets[i], Device));
        Device.SetRenderTargets(bindings);
    }

    public void Clear(Color color) { Check(); Device.Clear(color.ToNative()); }
    public void SetTexture(int slot, Texture2D texture) { Check(); Device.Textures[slot] = MonoGameResources.NativeTexture(texture, Device); }
    public void SetSampler(int slot, SamplerState sampler) { Check(); Device.SamplerStates[slot] = States.Get(sampler); }

    public void DrawFullscreen(Effect shader, BlendState blend, DepthStencilState depth, RasterizerState rasterizer)
    {
        Check();
        MonoGameResources.NativeEffect(shader, Device);
        Device.BlendState = States.Get(blend);
        Device.DepthStencilState = States.Get(depth);
        Device.RasterizerState = States.Get(rasterizer);
        Device.SetVertexBuffer(Quad);
        Device.Indices = Indices;
        if (shader == null) return;
        foreach (var pass in shader.CurrentTechnique.Passes)
        {
            pass.Apply();
            Device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
        }
    }

    public void BeginSprites(SpriteSortMode sort, BlendState blend, SamplerState sampler,
        DepthStencilState depth = null, RasterizerState rasterizer = null, Effect shader = null, Matrix? transform = null)
    {
        Check();
        Sprites.Begin(MonoGameRenderStates.ToNative(sort), States.Get(blend), States.Get(sampler),
            States.Get(depth), States.Get(rasterizer), MonoGameResources.NativeEffect(shader, Device), transform);
    }

    public void EndSprites() { Check(); Sprites.End(); }
    public void DrawSprite(Texture2D texture, Rectangle destination, Rectangle? source, Color color,
        float rotation = 0, Vector2 origin = default, SpriteEffects effects = SpriteEffects.None, float depth = 0)
    {
        Check();
        Sprites.Draw(MonoGameResources.NativeTexture(texture, Device), destination.ToNative(), source.ToNative(),
            color.ToNative(), rotation, origin, MonoGameRenderStates.ToNative(effects), depth);
    }

    public void DrawSprite(Texture2D texture, Vector2 position, Rectangle? source, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
    {
        Check();
        Sprites.Draw(MonoGameResources.NativeTexture(texture, Device), position, source.ToNative(), color.ToNative(),
            rotation, origin, scale, MonoGameRenderStates.ToNative(effects), depth);
    }

    public void DrawText(FontFace font, string text, Vector2 position, Color color, float scale)
    {
        Check();
        MonoGameFontFace.Draw(Sprites, font, text, position, color, scale);
    }

    private void Check() => ObjectDisposedException.ThrowIf(Disposed, this);
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Quad.Dispose();
        Indices.Dispose();
        States.Dispose();
    }
}
