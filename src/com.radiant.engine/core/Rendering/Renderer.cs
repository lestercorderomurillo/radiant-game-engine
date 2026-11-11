using com.radiant.engine.runtime;
using System;
using com.radiant.engine.graphics;

namespace com.radiant.engine.core;

/// <summary>
/// Fluent Radiant rendering API providing shader management, render targets,
/// and fullscreen quad drawing for post-processing effects.
///
/// <example>
/// Basic fullscreen shader pass:
/// <code>
/// Renderer
///     .Reset()
///     .SetShader("Effects/Blur")
///     .Configure((0, SamplerState.LinearClamp))
///     .SetTarget(outputTexture)
///     .Clear(Color.Black)
///     .SetParameter("InputTexture", inputTexture)
///     .SetParameter("BlurRadius", 5.0f)
///     .Draw()
///     .Commit();
/// </code>
/// </example>
///
/// <example>
/// Multiple render target (MRT) rendering:
/// <code>
/// Renderer
///     .Reset()
///     .SetShader("GBuffer/Generate")
///     .SetTargets(albedoRT, normalRT, depthRT)
///     .Clear(Color.Black)
///     .Draw()
///     .Commit();
/// </code>
/// </example>
///
/// <example>
/// Texture drawing with shader:
/// <code>
/// Renderer
///     .Reset()
///     .SetShader("Effects/ColorGrade")
///     .Configure(BlendState.AlphaBlend)
///     .SetTarget(outputTexture)
///     .DrawTexture(inputTexture, Vector2.Zero)
///     .Commit();
/// </code>
/// </example>
/// </summary>
public partial class Renderer : IRenderer
{
    public IWindow Window { get; }
    public GameLoop GameLoop { get; }
    private readonly IGraphicsBackend Backend;
    private bool Disposed;

    /// <summary>Scene render target that replaces the backbuffer as the main render surface.</summary>
    public RenderTarget2D SceneRT { get; private set; }

    /// <summary>True if currently between Begin/Draw and Commit calls.</summary>
    public bool IsDrawing { get; private set; }

    /// <summary>Name of the currently active shader (null if none).</summary>
    public string CurrentShaderName { get; private set; }

    private readonly com.radiant.engine.graphics.IGraphicsResourceFactory Resources;
    private readonly ShaderRegistry Shaders;
    private readonly TextureManager Textures;
    private readonly FontRegistry Fonts;
    private readonly ShapeBatcher ShapeBatch;
    private readonly IGeometryBackend Geometry;
    private readonly IGraphicsCommands Commands;

    private readonly RenderView View = new(3840, 2160);
    public int ScreenWidth => View.ScreenWidth;
    public int ScreenHeight => View.ScreenHeight;
    public Vector2 ScreenSize => View.ScreenSize;
    public float AspectRatio => View.AspectRatio;
    public float InverseAspectRatio => View.InverseAspectRatio;
    public float VirtualWidth => View.VirtualWidth;
    public float VirtualHeight => View.VirtualHeight;
    public Vector2 VirtualSize => View.VirtualSize;
    public float ScreenDiagonal => View.ScreenDiagonal;
    public int ScreenArea => View.ScreenArea;
    public int ScreenLowerPowerOfTwo => View.ScreenLowerPowerOfTwo;
    public int ScreenHigherPowerOfTwo => View.ScreenHigherPowerOfTwo;
    public Vector2 ScreenSizeLowerPowerOfTwo => View.ScreenSizeLowerPowerOfTwo;
    public Vector2 ScreenSizeHigherPowerOfTwo => View.ScreenSizeHigherPowerOfTwo;
    public Vector2 VirtualToScreenScale => View.VirtualToScreenScale;
    public int ScaledWidth => View.ScaledWidth;
    public int ScaledHeight => View.ScaledHeight;
    public Vector2 ScaledSize => View.ScaledSize;
    public int ScaledHigherPowerOfTwo => View.ScaledHigherPowerOfTwo;
    public float RenderScale { get => View.RenderScale; set => View.RenderScale = value; }
    public event Action<float> RenderScaleChanged
    {
        add => View.RenderScaleChanged += value;
        remove => View.RenderScaleChanged -= value;
    }

    /// <summary>
    /// Creates a renderer from portable graphics and host services.
    /// </summary>
    /// <param name="backend">Owned graphics session.</param>
    /// <param name="window">Portable window information.</param>
    /// <param name="gameLoop">Owning ECS loop.</param>
    public Renderer(IGraphicsBackend backend, IWindow window, GameLoop gameLoop)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(gameLoop);
        Backend = backend;
        Window = window;
        GameLoop = gameLoop;
        Resources = backend.Resources;
        Commands = backend.Commands;
        Geometry = backend.Geometry;
        Shaders = new ShaderRegistry(backend.Assets);
        Textures = new TextureManager(Resources, backend.Assets);
        Fonts = new FontRegistry(backend.Fonts);
        ShapeBatch = new ShapeBatcher(backend.Shapes);
        try
        {
            for (int i = 0; i < SamplerStates.Length; i++) SamplerStates[i] = SamplerState.LinearClamp;
            InitializeFonts();
            UpdateScreenInfo();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Loads the font families used by renderer text commands.</summary>
    private void InitializeFonts()
    {
        Fonts.Load("Inter", "fonts/Inter-Regular.ttf");
        Fonts.Load("Inter-Bold", "fonts/Inter-Bold.ttf");
    }

    /// <summary>Refreshes the portable view from the presentation surface, never an offscreen target.</summary>
    public void UpdateScreenInfo()
    {
        var presentation = Backend.PresentationSize;
        // A minimized surface has no drawable size; preserve the last valid view.
        if (presentation.X <= 0 || presentation.Y <= 0) return;
        View.Resize(presentation.X, presentation.Y);
        ShapeBatch.ViewProjection = View.ViewProjection;
    }

    public Rectangle? ScenePresentationBounds { get; set; }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        if (ScenePresentationBounds is Rectangle bounds && bounds.Width > 0 && bounds.Height > 0)
            screenPos = (screenPos - new Vector2(bounds.X, bounds.Y)) * new Vector2((float)ScreenWidth / bounds.Width, (float)ScreenHeight / bounds.Height);
        return View.ScreenToWorld(screenPos);
    }
    public Vector2 WorldToScreen(Vector2 worldPos) => View.WorldToScreen(worldPos);
    public Rectangle VirtualToScreenRect(float x, float y, float width, float height) => View.VirtualToScreenRect(x, y, width, height);

    /// <summary>Marks the renderer as actively drawing (rarely needed directly).</summary>
    public IRenderer Begin()
    {
        IsDrawing = true;
        return this;
    }

    /// <summary>
    /// Commits any pending SpriteBatch operations and marks drawing complete.
    /// Call at the end of each render pass.
    /// </summary>
    public IRenderer Commit()
    {
        CommitTextures();
        IsDrawing = false;
        return this;
    }

    /// <summary>
    /// Resets all render state to defaults and clears the current shader.
    /// Call at the start of each render pass for clean state.
    /// </summary>
    public IRenderer Reset()
    {
        CommitTextures();

        BlendState = BlendState.Opaque;
        DepthStencilState = DepthStencilState.None;
        RasterizerState = RasterizerState.CullNone;
        SpriteSortMode = SpriteSortMode.Immediate;
        SamplerDirtyMask = 0;

        CurrentShader = null;
        CurrentShaderName = null;
        IsDrawing = false;

        return this;
    }

    /// <summary>Whether the game window is active and focused.</summary>
    public bool IsActive => Window.IsActive;

    /// <summary>Current viewport bounds rectangle.</summary>
    public Rectangle ViewportBounds => Commands.ViewportBounds;

    /// <summary>Whether the window has a pending resize that needs handling.</summary>
    public bool HasPendingResize => Backend.HasPendingResize;

    /// <summary>Handles a pending window resize: clears the flag and updates screen info.</summary>
    public void HandleResize()
    {
        Backend.AcknowledgeResize();
        UpdateScreenInfo();
    }

    /// <summary>Clears the backbuffer with the specified color. Routes rendering to SceneRT.</summary>
    public void ClearBackBuffer(Color color)
    {
        if (SceneRT == null || SceneRT.Width != ScreenWidth || SceneRT.Height != ScreenHeight)
        {
            SceneRT?.Dispose();
            SceneRT = CreateRenderTarget(ScreenWidth, ScreenHeight, usage: RenderTargetUsage.PreserveContents);
        }

        BindTarget(SceneRT);
        Commands.Clear(color);
    }

    /// <summary>Copies SceneRT to the actual backbuffer for presentation.</summary>
    public void PresentToBackBuffer()
    {
        if (SceneRT == null) return;

        CommitTextures();
        Commands.SetTargets(ReadOnlySpan<RenderTarget2D>.Empty);
        CurrentTargets = Array.Empty<RenderTarget2D>();
        Commands.BeginSprites(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
        Commands.DrawSprite(SceneRT, Commands.ViewportBounds, null, Color.White);
        Commands.EndSprites();
    }

    /// <summary>
    /// Creates a backend-owned render target.
    /// </summary>
    public RenderTarget2D CreateRenderTarget(int width, int height,
        SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None,
        RenderTargetUsage usage = RenderTargetUsage.DiscardContents, int samples = 0)
    {
        return Resources.CreateRenderTarget(width, height, format, depth, usage, samples);
    }

    /// <summary>Finds a supported multisample count for the requested render-target format.</summary>
    public int GetSupportedSamples(int requested, SurfaceFormat format = SurfaceFormat.Color, DepthFormat depth = DepthFormat.None)
    {
        return Resources.GetSupportedSamples(requested, format, depth);
    }

    /// <summary>
    /// Creates a backend-owned texture.
    /// </summary>
    public Texture2D CreateTexture(int width, int height, SurfaceFormat format = SurfaceFormat.Color)
    {
        return Resources.CreateTexture(width, height, format);
    }

    /// <summary>
    /// Loads and caches a content texture by name. Returns the same instance for repeated calls.
    /// </summary>
    /// <param name="name">Asset name relative to Content root (e.g., "Ghost", "sprites/Player").</param>
    /// <returns>The cached Texture2D.</returns>
    public Texture2D GetTexture(string name) => Textures.Get(name);

    /// <summary>
    /// Gets or creates a cached solid color texture.
    /// </summary>
    /// <param name="color">Fill color for the texture.</param>
    /// <param name="width">Texture width (default 1).</param>
    /// <param name="height">Texture height (default 1).</param>
    /// <returns>Cached texture with the specified color.</returns>
    public Texture2D GetSolidTexture(Color color, int width = 1, int height = 1) => Textures.GetSolid(color, width, height);

    /// <summary>
    /// Gets or creates a cached anti-aliased circle texture.
    /// </summary>
    /// <param name="diameter">Circle diameter in pixels.</param>
    /// <returns>Cached white circle texture with premultiplied alpha.</returns>
    public Texture2D GetCircleTexture(int diameter) => Textures.GetShape("Circle", diameter);

    /// <summary>
    /// Gets or creates a cached anti-aliased downward-pointing triangle texture.
    /// </summary>
    /// <param name="size">Texture size in pixels (square).</param>
    /// <returns>Cached white triangle texture with premultiplied alpha.</returns>
    public Texture2D GetTriangleTexture(int size) => Textures.GetShape("Triangle", size);

    /// <summary>
    /// Gets the cached checkmark icon texture (loaded from content pipeline).
    /// </summary>
    /// <param name="size">Ignored — retained for backward compatibility. Icon is a fixed-size PNG.</param>
    public Texture2D GetCheckmarkTexture(int size) => GetTexture("presets/icons/Checkmark");

    /// <summary>
    /// Gets the cached search (magnifying glass) icon texture (loaded from content pipeline).
    /// </summary>
    /// <param name="size">Ignored — retained for backward compatibility. Icon is a fixed-size PNG.</param>
    public Texture2D GetSearchTexture(int size) => GetTexture("presets/icons/Search");

    /// <summary>
    /// Gets the cached trash can icon texture (loaded from content pipeline).
    /// </summary>
    /// <param name="size">Ignored — retained for backward compatibility. Icon is a fixed-size PNG.</param>
    public Texture2D GetTrashTexture(int size) => GetTexture("presets/icons/Trash");

    /// <summary> Gets the cached visibility (eye open) icon texture. </summary>
    public Texture2D GetVisibilityTexture() => GetTexture("presets/icons/Visibility");

    /// <summary> Gets the cached visibility off (eye closed) icon texture. </summary>
    public Texture2D GetVisibilityOffTexture() => GetTexture("presets/icons/VisibilityOff");

    /// <summary>
    /// Gets or creates a cached anti-aliased rounded rectangle texture for 9-slice rendering.
    /// The texture is (radius*2+2) pixels square with premultiplied alpha SDF corners.
    /// </summary>
    /// <param name="radius">Corner radius in pixels.</param>
    /// <returns>Cached white rounded rect texture with premultiplied alpha.</returns>
    public Texture2D GetRoundedRectTexture(int radius) => Textures.GetShape("RoundedRect", radius);

    /// <summary>
    /// Registers a procedural shape texture generator. The generator receives the size parameter
    /// and must return pixel data (width, height, pixels). The Renderer creates and caches the
    /// GPU texture internally.
    /// </summary>
    /// <param name="name">Unique shape name for later retrieval.</param>
    /// <param name="generator">Function that produces pixel data for a given size.</param>
    /// <param name="minSize">Minimum size clamp (requests below this are clamped up).</param>
    public void RegisterShapeTexture(string name, Func<int, (int Width, int Height, Color[] Pixels)> generator, int minSize = 1)
    {
        Textures.RegisterShape(name, (resources, size) =>
        {
            var (width, height, pixels) = generator(size);

            var texture = resources.CreateTexture(width, height);
            texture.SetData(pixels);

            return texture;
        }, minSize);
    }

    /// <summary>
    /// Gets or creates a cached procedural shape texture by name and size.
    /// </summary>
    /// <param name="name">Shape name as registered via RegisterShapeTexture.</param>
    /// <param name="size">Requested size (clamped to the shape's minimum).</param>
    public Texture2D GetShapeTexture(string name, int size) => Textures.GetShape(name, size);

    /// <summary>
    /// Supersample multiplier for font rendering. Fonts rasterize at size * FontRenderScale
    /// and draw scaled down for sharp, anti-aliased text.
    /// </summary>
    public float FontRenderScale
    {
        get => Fonts.FontRenderScale;
        set => Fonts.FontRenderScale = value;
    }

    /// <summary>
    /// Loads a TTF font family into the font system. Path is relative to Content root.
    /// </summary>
    public void LoadFont(string name, string path) => Fonts.Load(name, path);

    /// <summary>
    /// Gets a dynamic font at a specific pixel size (raw, no supersampling applied).
    /// </summary>
    public FontFace GetFont(string name, float size) => Fonts.GetFont(name, size);

    /// <summary>
    /// Measures text dimensions using a dynamic font at a specific pixel size.
    /// Accounts for FontRenderScale internally — returns dimensions at the requested size.
    /// </summary>
    public Vector2 MeasureString(string fontName, float size, string text) => Fonts.Measure(fontName, size, text);

    /// <summary>
    /// Gets the line height for a font at a specific size, accounting for FontRenderScale.
    /// </summary>
    public float GetLineHeight(string fontName, float size) => Fonts.GetLineHeight(fontName, size);

    /// <summary>
    /// Draws text using a dynamic font at a specific size. Call between BeginDraw/EndDraw.
    /// Supersampled via FontRenderScale for sharp rendering.
    /// </summary>
    public void DrawString(string fontName, float size, string text, Vector2 position, Color color, bool bold = false)
    {
        float scale = 1f / Fonts.FontRenderScale;
        var font = Fonts.GetFont(fontName, size * Fonts.FontRenderScale);
        Commands.DrawText(font, text, position, color, scale);
        if (bold) Commands.DrawText(font, text, position + Vector2.UnitX, color, scale);
    }

    /// <summary>Disposes all cached resources (shaders, textures, buffers).</summary>
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        SceneRT?.Dispose();

        Shaders?.Dispose();
        Textures?.Dispose();
        Fonts?.Dispose();
        Backend.Dispose();
    }
}
