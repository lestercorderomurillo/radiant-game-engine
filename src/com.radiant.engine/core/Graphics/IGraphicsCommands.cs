using System;

namespace com.radiant.engine.graphics;

/// <summary>Synchronous render-thread commands. All resources must belong to this backend.</summary>
public interface IGraphicsCommands : IDisposable
{
    Rectangle ViewportBounds { get; }
    /// <summary>Binds targets; an empty span selects the presentation backbuffer. Does not retain the span.</summary>
    void SetTargets(ReadOnlySpan<RenderTarget2D> targets);
    void Clear(Color color);
    void SetTexture(int slot, Texture2D texture);
    void SetSampler(int slot, SamplerState sampler);
    void DrawFullscreen(Effect shader, BlendState blend, DepthStencilState depth, RasterizerState rasterizer);
    void BeginSprites(SpriteSortMode sort, BlendState blend, SamplerState sampler,
        DepthStencilState depth = null, RasterizerState rasterizer = null, Effect shader = null, Matrix? transform = null);
    void EndSprites();
    void DrawSprite(Texture2D texture, Rectangle destination, Rectangle? source, Color color,
        float rotation = 0, Vector2 origin = default, SpriteEffects effects = SpriteEffects.None, float depth = 0);
    void DrawSprite(Texture2D texture, Vector2 position, Rectangle? source, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth);
    void DrawText(FontFace font, string text, Vector2 position, Color color, float scale);
}
