using System;
using com.radiant.engine.graphics;

namespace com.radiant.engine.core;

/// <summary>
/// Draws everything on screen: shader passes, batched shapes, sprites, text and render targets.
/// Every scene and system gets one through its <c>Renderer</c> property, and most calls return the
/// renderer so a pass reads as a single chain.
/// </summary>
/// <example>
/// The following example shows how to run a fullscreen shader pass into a render target:
/// <code>
/// Renderer
///     .Reset()
///     .SetShader("Effects/Blur")
///     .Configure(SamplerState.LinearClamp)
///     .SetTarget(blurred).Clear(Color.Black)
///     .SetParameter("InputTexture", source)
///     .SetParameter("BlurRadius", 4f)
///     .Draw()
///     .Commit();
/// </code>
/// The following example shows how to draw a few shapes and flush them in one draw call:
/// <code>
/// Renderer.DrawRect(new Vector2(40, 40), new Vector2(200, 80), Color.White);
/// Renderer.DrawCircle(new Vector2(400, 300), 32f, Color.Orange);
/// Renderer.FlushShapes();
/// </code>
/// </example>
public interface IRenderer : IDisposable, IGraphicsResourceFactory, IComputeRenderer, IAcceleratedRenderer
{
    /// <summary>The window being drawn into: title, size and focus.</summary>
    runtime.IWindow Window { get; }
    /// <summary>The surface the scene renders into before it is presented. Use it as a source for full-screen effects.</summary>
    RenderTarget2D SceneRT { get; }
    /// <summary>True between <c>Begin</c> or <c>Draw</c> and the matching <c>Commit</c>.</summary>
    bool IsDrawing { get; }
    /// <summary>Name of the shader selected with <c>SetShader</c>, or null when none is active.</summary>
    string CurrentShaderName { get; }
    /// <summary>Width of the window's drawable area, in pixels.</summary>
    int ScreenWidth { get; }
    /// <summary>Height of the window's drawable area, in pixels.</summary>
    int ScreenHeight { get; }
    /// <summary>Size of the window's drawable area, in pixels.</summary>
    Vector2 ScreenSize { get; }
    /// <summary>Screen width divided by height.</summary>
    float AspectRatio { get; }
    /// <summary>Screen height divided by width.</summary>
    float InverseAspectRatio { get; }
    /// <summary>Width of the fixed virtual world space (3840) that world coordinates are expressed in.</summary>
    float VirtualWidth { get; }
    /// <summary>Height of the fixed virtual world space (2160) that world coordinates are expressed in.</summary>
    float VirtualHeight { get; }
    /// <summary>Size of the fixed virtual world space. <c>VirtualSize / 2</c> is the center of the default world.</summary>
    Vector2 VirtualSize { get; }
    /// <summary>Length of the screen diagonal, in pixels.</summary>
    float ScreenDiagonal { get; }
    /// <summary>Number of pixels on screen (width × height).</summary>
    int ScreenArea { get; }
    /// <summary>Largest power of two that fits within the larger screen dimension.</summary>
    int ScreenLowerPowerOfTwo { get; }
    /// <summary>Smallest power of two that covers the larger screen dimension.</summary>
    int ScreenHigherPowerOfTwo { get; }
    /// <summary>Screen size rounded down to powers of two, per axis.</summary>
    Vector2 ScreenSizeLowerPowerOfTwo { get; }
    /// <summary>Screen size rounded up to powers of two, per axis.</summary>
    Vector2 ScreenSizeHigherPowerOfTwo { get; }
    /// <summary>Scale from virtual units to screen pixels. Multiply UI sizes by it to keep them proportional to the window.</summary>
    Vector2 VirtualToScreenScale { get; }
    /// <summary>Internal resolution as a fraction of the screen, from 0.25 to 1. Lower it to trade sharpness for speed.</summary>
    float RenderScale { get; set; }
    /// <summary>Raised with the new value whenever <c>RenderScale</c> changes, so you can recreate scaled targets.</summary>
    event Action<float> RenderScaleChanged;
    /// <summary>Screen width multiplied by <c>RenderScale</c>.</summary>
    int ScaledWidth { get; }
    /// <summary>Screen height multiplied by <c>RenderScale</c>.</summary>
    int ScaledHeight { get; }
    /// <summary>Screen size multiplied by <c>RenderScale</c>.</summary>
    Vector2 ScaledSize { get; }
    /// <summary>Smallest power of two that covers the larger scaled dimension.</summary>
    int ScaledHigherPowerOfTwo { get; }
    /// <summary>Re-reads the window size. The engine calls this after a resize; call it yourself only if you resize manually.</summary>
    void UpdateScreenInfo();
    /// <summary>Converts a pixel position, such as the mouse, into world coordinates.</summary>
    Vector2 ScreenToWorld(Vector2 screenPos);
    /// <summary>Converts a world position into window pixels, for example to place a label over an entity.</summary>
    Vector2 WorldToScreen(Vector2 worldPos);
    /// <summary>Converts a rectangle in virtual units into window pixels.</summary>
    Rectangle VirtualToScreenRect(float x, float y, float width, float height);
    /// <summary>Marks the start of a drawing pass. <c>Draw</c> does this for you, so you rarely need it.</summary>
    IRenderer Begin();
    /// <summary>Finishes the current pass and submits pending work. End every chain with it.</summary>
    IRenderer Commit();
    /// <summary>Clears the shader, render states and texture bindings. Start every pass with it so earlier passes can't leak state.</summary>
    IRenderer Reset();
    /// <summary>True while the window has focus. Skip keyboard handling when it is false.</summary>
    bool IsActive { get; }
    /// <summary>The loop driving this window; adjust frame and update rates through it.</summary>
    runtime.GameLoop GameLoop { get; }
    /// <summary>Bounds of the active viewport, in pixels.</summary>
    Rectangle ViewportBounds { get; }
    /// <summary>True when the window was resized and size-dependent resources have not been refreshed yet.</summary>
    bool HasPendingResize { get; }
    /// <summary>Applies a pending resize. The engine calls it before systems update; systems react in <c>OnResize</c>.</summary>
    void HandleResize();
    /// <summary>Clears the scene surface to a color at the start of a frame.</summary>
    void ClearBackBuffer(Color color);
    /// <summary>Copies the scene surface to the window. The engine calls this at the end of each frame.</summary>
    void PresentToBackBuffer();
    /// <summary>Loads a texture from <c>Content</c> by name, such as <c>"presets/icons/Search"</c>. Repeated calls return the same instance.</summary>
    Texture2D GetTexture(string name);
    /// <summary>Returns a cached texture filled with one color. Handy for tinting quads and drawing bars.</summary>
    Texture2D GetSolidTexture(Color color, int width = 1, int height = 1);
    /// <summary>Returns a cached antialiased circle of the given diameter.</summary>
    Texture2D GetCircleTexture(int diameter);
    /// <summary>Returns a cached antialiased triangle pointing down, used for dropdown arrows.</summary>
    Texture2D GetTriangleTexture(int size);
    /// <summary>Returns the cached white checkmark icon.</summary>
    Texture2D GetCheckmarkTexture(int size);
    /// <summary>Returns the cached white magnifying-glass icon.</summary>
    Texture2D GetSearchTexture(int size);
    /// <summary>Returns the cached white trash-can icon.</summary>
    Texture2D GetTrashTexture(int size);
    /// <summary>Returns the cached open-eye icon.</summary>
    Texture2D GetVisibilityTexture();
    /// <summary>Returns the cached closed-eye icon.</summary>
    Texture2D GetVisibilityOffTexture();
    /// <summary>Returns a cached rounded-corner texture used to draw rounded rectangles of any size.</summary>
    Texture2D GetRoundedRectTexture(int radius);
    /// <summary>
    /// Registers a procedural texture. Your generator receives a size and returns the pixels; the result is
    /// cached per size and retrieved with <c>GetShapeTexture</c>.
    /// </summary>
    /// <example>
    /// The following example shows how to register a filled diamond and draw it at 32 pixels:
    /// <code>
    /// Renderer.RegisterShapeTexture("Diamond", size =>
    /// {
    ///     var pixels = new Color[size * size];
    ///     float half = size / 2f;
    ///     for (int y = 0; y &lt; size; y++)
    ///         for (int x = 0; x &lt; size; x++)
    ///             if (MathF.Abs(x - half) + MathF.Abs(y - half) &lt;= half) pixels[y * size + x] = Color.White;
    ///     return (size, size, pixels);
    /// });
    ///
    /// var diamond = Renderer.GetShapeTexture("Diamond", 32);
    /// </code>
    /// </example>
    void RegisterShapeTexture(string name, Func<int, (int Width, int Height, Color[] Pixels)> generator, int minSize = 1);
    /// <summary>Returns a texture created by a generator registered with <c>RegisterShapeTexture</c>.</summary>
    Texture2D GetShapeTexture(string name, int size);
    /// <summary>Oversampling factor for text. Fonts rasterize at <c>size × FontRenderScale</c> and draw scaled down, which keeps them crisp on high-DPI screens.</summary>
    float FontRenderScale { get; set; }
    /// <summary>Loads a TTF file (path relative to <c>Content</c>) under a name you then pass to <c>DrawString</c>. <c>Inter</c> and <c>Inter-Bold</c> are preloaded.</summary>
    void LoadFont(string name, string path);
    /// <summary>Returns a loaded font at a pixel size, for custom text layout.</summary>
    FontFace GetFont(string name, float size);
    /// <summary>Measures text at a pixel size. Use it to center or right-align labels.</summary>
    Vector2 MeasureString(string fontName, float size, string text);
    /// <summary>Returns the distance between baselines for a font at a pixel size.</summary>
    float GetLineHeight(string fontName, float size);
    /// <summary>Draws text at any pixel size. Call between <c>BeginDraw</c> and <c>EndDraw</c>.</summary>
    /// <example>
    /// The following example shows how to center a title horizontally:
    /// <code>
    /// var size = Renderer.MeasureString("Inter-Bold", 32f, "Paused");
    /// var position = new Vector2((Renderer.ScreenWidth - size.X) / 2, 48);
    ///
    /// Renderer.BeginDraw();
    /// Renderer.DrawString("Inter-Bold", 32f, "Paused", position, Color.White);
    /// Renderer.EndDraw();
    /// </code>
    /// </example>
    void DrawString(string fontName, float size, string text, Vector2 position, Color color, bool bold = false);
    /// <summary>Runs the active shader over the whole current target. Set the shader, target and parameters first.</summary>
    IRenderer Draw();
    /// <summary>Draws a texture at a position with the active shader and states.</summary>
    IRenderer DrawTexture(Texture2D texture, Vector2 position);
    /// <summary>Draws a tinted texture at a position with the active shader and states.</summary>
    IRenderer DrawTexture(Texture2D texture, Vector2 position, Color color);
    /// <summary>Draws a texture stretched to a rectangle with the active shader and states.</summary>
    IRenderer DrawTexture(Texture2D texture, Rectangle destination);
    /// <summary>Draws a tinted texture stretched to a rectangle with the active shader and states.</summary>
    IRenderer DrawTexture(Texture2D texture, Rectangle destination, Color color);
    /// <summary>Draws part of a texture, such as a sprite-sheet frame, into a rectangle.</summary>
    IRenderer DrawTexture(Texture2D texture, Rectangle destination, Rectangle? source, Color color);
    /// <summary>Draws a texture with full control over source region, rotation, origin, scale, flipping and depth.</summary>
    IRenderer DrawTexture(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth);
    /// <summary>Stretches a texture over the entire current target. Use it to show a finished image or composite a layer.</summary>
    void Blit(Texture2D source, BlendState blend = null, SamplerState sampler = null);
    /// <summary>Copies a texture into another target at its native size, anchored at the top-left corner, without stretching.</summary>
    void Blit(Texture2D source, RenderTarget2D target, Color? clearColor = null,
        BlendState blend = null, SamplerState sampler = null);
    /// <summary>Starts a sprite and text session. Pass <c>WorldTransform</c> as the transform to draw in world coordinates.</summary>
    void BeginDraw(SpriteSortMode sort = SpriteSortMode.Deferred,
        BlendState blend = null, SamplerState sampler = null, Matrix? transform = null);
    /// <summary>Draws a texture into a rectangle. Call between <c>BeginDraw</c> and <c>EndDraw</c>.</summary>
    void DrawSprite(Texture2D texture, Rectangle destination, Color color);
    /// <summary>Draws a texture into a rectangle with extra opacity (0–1). Call between <c>BeginDraw</c> and <c>EndDraw</c>.</summary>
    void DrawSprite(Texture2D texture, Rectangle destination, Color color, float opacity);
    /// <summary>Draws part of a texture with rotation around an origin and optional flipping. Call between <c>BeginDraw</c> and <c>EndDraw</c>.</summary>
    void DrawSprite(Texture2D texture, Rectangle destination, Rectangle? source,
        Color color, float rotation = 0f, Vector2 origin = default, SpriteEffects effects = SpriteEffects.None);
    /// <summary>Ends the sprite and text session started by <c>BeginDraw</c> and submits it.</summary>
    void EndDraw();
    /// <summary>Erases the current target wherever the mask is opaque. Useful for cutting holes, such as fog of war.</summary>
    void BlitMask(Texture2D mask, Rectangle destination, float rotation = 0f, Vector2 origin = default);
    /// <summary>Draws a filled rectangle with rounded corners; choose which corners with <c>RoundedCorners</c>. Call between <c>BeginDraw</c> and <c>EndDraw</c>.</summary>
    void DrawRoundedRect(Rectangle bounds, Color color, int cornerRadius, RoundedCorners corners = RoundedCorners.All);
    /// <summary>Uploads pixels from the CPU into a target, for example a generated map or minimap.</summary>
    void UploadToTexture(RenderTarget2D target, Color[] data, int count = 0);
    /// <summary>Uploads pixels into a region of a target.</summary>
    void UploadToTexture(RenderTarget2D target, Color[] data, Rectangle region);
    /// <summary>Binds a texture to a shader register. Prefer <c>SetParameter</c> with the texture's name.</summary>
    IRenderer SetTexture(int slot, Texture2D texture);
    /// <summary>Binds a texture and its sampler to a shader register.</summary>
    IRenderer SetTexture(int slot, Texture2D texture, SamplerState sampler);
    /// <summary>Binds several textures to shader registers at once.</summary>
    IRenderer SetTextures(params (int slot, Texture2D texture)[] textures);
    /// <summary>Binds several textures and samplers to shader registers at once.</summary>
    IRenderer SetTextures(params (int slot, Texture2D texture, SamplerState sampler)[] textures);
    /// <summary>Unbinds the first texture slots so a target can be written after being read.</summary>
    IRenderer ClearTextures(int count = 4);
    /// <summary>Runs an iterative effect by alternating two targets, and returns the one holding the final result.</summary>
    /// <example>
    /// The following example shows how to run four blur passes:
    /// <code>
    /// var result = Renderer.PingPong(first, second, passes: 4,
    ///     beforePass: (index, input) => Renderer
    ///         .SetShader("Effects/Blur")
    ///         .SetParameter("InputTexture", input)
    ///         .SetParameter("Offset", index + 1f));
    /// </code>
    /// </example>
    RenderTarget2D PingPong(
        RenderTarget2D a,
        RenderTarget2D b,
        int passes,
        Action<int, RenderTarget2D> beforePass = null,
        Action<int> afterPass = null,
        Color? clearColor = null);
    /// <summary>Draws a mesh with the active shader and render states.</summary>
    IRenderer DrawMesh(Mesh mesh);
    // Compatibility aliases; new integrations use the backend-independent capabilities.
    /// <summary>Same as <c>SupportsPathTracing</c>.</summary>
    bool SupportsVulkanPathTracing(out string reason) => SupportsPathTracing(out reason);
    /// <summary>Same as <c>SupportsDenoising</c>.</summary>
    bool SupportsNrdDenoising(out string reason) => SupportsDenoising(out reason);
    /// <summary>Selects a shader by its path under <c>Content/shaders</c>, such as <c>"HRC/HRC_FluenceSum"</c>. Loaded once, then cached.</summary>
    IRenderer SetShader(string name);
    /// <summary>Returns a shader without selecting it, so you can set its parameters ahead of time.</summary>
    Effect GetShaderEffect(string name);
    /// <summary>Unloads a cached shader. The next <c>SetShader</c> loads it again.</summary>
    IRenderer ReleaseShader(string name);
    /// <summary>Selects a technique within the active shader.</summary>
    IRenderer SetTechnique(string technique);
    /// <summary>Sets how drawing blends with the target, for example <c>BlendState.Additive</c> for light.</summary>
    IRenderer Configure(BlendState state);
    /// <summary>Sets depth and stencil testing for the next draws.</summary>
    IRenderer Configure(DepthStencilState state);
    /// <summary>Sets culling, fill mode and scissor behavior for the next draws.</summary>
    IRenderer Configure(RasterizerState state);
    /// <summary>Sets how a texture slot is filtered and wrapped, for example <c>SamplerState.PointClamp</c> for pixel art.</summary>
    IRenderer Configure(SamplerState state, int slot = 0);
    /// <summary>Sets the sprite ordering used by texture draws.</summary>
    IRenderer Configure(SpriteSortMode mode);
    /// <summary>Sets samplers for two slots at once.</summary>
    IRenderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1);
    /// <summary>Sets samplers for three slots at once.</summary>
    IRenderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1, (int slot, SamplerState state) s2);
    /// <summary>Sets samplers for four slots at once.</summary>
    IRenderer Configure((int slot, SamplerState state) s0, (int slot, SamplerState state) s1, (int slot, SamplerState state) s2, (int slot, SamplerState state) s3);
    /// <summary>Sets samplers for any number of slots.</summary>
    IRenderer Configure(params (int slot, SamplerState state)[] samplers);
    /// <summary>Applies a mix of blend, depth, rasterizer and sampler states in one call.</summary>
    IRenderer Configure(params object[] states);
    /// <summary>Sets a shader parameter on the active shader, or on <paramref name="shader"/> when given.</summary>
    IRenderer SetParameter(string name, float value, Effect shader = null);
    /// <summary>Sets an integer shader parameter.</summary>
    IRenderer SetParameter(string name, int value, Effect shader = null);
    /// <summary>Sets a boolean shader parameter.</summary>
    IRenderer SetParameter(string name, bool value, Effect shader = null);
    /// <summary>Sets a two-component shader parameter, such as a screen size.</summary>
    IRenderer SetParameter(string name, Vector2 value, Effect shader = null);
    /// <summary>Sets a three-component shader parameter, such as a color or direction.</summary>
    IRenderer SetParameter(string name, Vector3 value, Effect shader = null);
    /// <summary>Sets a four-component shader parameter.</summary>
    IRenderer SetParameter(string name, Vector4 value, Effect shader = null);
    /// <summary>Sets a matrix shader parameter, such as a view-projection.</summary>
    IRenderer SetParameter(string name, Matrix value, Effect shader = null);
    /// <summary>Binds a texture to a named shader parameter.</summary>
    IRenderer SetParameter(string name, Texture2D value, Effect shader = null);
    /// <summary>Sets a float array shader parameter.</summary>
    IRenderer SetParameter(string name, float[] value, Effect shader = null);
    /// <summary>Sets a two-component array shader parameter.</summary>
    IRenderer SetParameter(string name, Vector2[] value, Effect shader = null);
    /// <summary>Sets a three-component array shader parameter.</summary>
    IRenderer SetParameter(string name, Vector3[] value, Effect shader = null);
    /// <summary>Sets a four-component array shader parameter.</summary>
    IRenderer SetParameter(string name, Vector4[] value, Effect shader = null);
    /// <summary>Sets a matrix array shader parameter.</summary>
    IRenderer SetParameter(string name, Matrix[] value, Effect shader = null);
    /// <summary>Sets several parameters in one call; each value's type picks the matching overload.</summary>
    IRenderer SetParameter(Effect shader = null, params (string name, object value)[] parameters);
    /// <summary>Adds a custom shape to the batch. Call <c>FlushShapes</c> to draw it.</summary>
    IRenderer DrawShape(Shape shape);
    /// <summary>Adds a filled rectangle to the batch. Call <c>FlushShapes</c> to draw it.</summary>
    IRenderer DrawRect(Vector2 position, Vector2 size, Color color);
    /// <summary>Adds a filled rectangle to the batch. Call <c>FlushShapes</c> to draw it.</summary>
    IRenderer DrawRect(float x, float y, float width, float height, Color color);
    /// <summary>Adds a filled circle to the batch. Call <c>FlushShapes</c> to draw it.</summary>
    IRenderer DrawCircle(Vector2 center, float radius, Color color);
    /// <summary>Adds a filled circle to the batch. Call <c>FlushShapes</c> to draw it.</summary>
    IRenderer DrawCircle(float x, float y, float radius, Color color);
    /// <summary>Adds a filled triangle to the batch. Call <c>FlushShapes</c> to draw it.</summary>
    IRenderer DrawTriangle(Vector2 position, Vector2 size, Color color);
    /// <summary>Adds a triangle outline to the batch. Call <c>FlushShapes</c> to draw it.</summary>
    IRenderer DrawTriangleBorder(Vector2 position, Vector2 size, Color color);
    /// <summary>Draws every batched shape in one call and empties the batch. Techniques: <c>"Default"</c> (smooth edges), <c>"Sharp"</c> and <c>"Emissive"</c>.</summary>
    IRenderer FlushShapes(RenderTarget2D target = null, Color? clearColor = null, string technique = "Default");
    /// <summary>Discards batched shapes without drawing them.</summary>
    IRenderer ClearShapes();
    /// <summary>Number of shapes waiting in the batch.</summary>
    int ShapeBatchCount { get; }
    /// <summary>Draws shapes straight from your own array, without copying them into the batch.</summary>
    IRenderer FlushShapesExternal(Shape[] shapes, int count, RenderTarget2D target = null, Color? clearColor = null, string technique = "Default");
    /// <summary>CPU milliseconds spent uploading the last shape batch.</summary>
    float LastSetDataMs { get; }
    /// <summary>CPU milliseconds spent submitting the last shape batch.</summary>
    float LastDrawMs { get; }
    /// <summary>Prepares one shape buffer per worker thread so ECS queries can emit shapes in parallel.</summary>
    IRenderer InitializeParallelShapes(int threadCount, int capacityPerThread = 16384);
    /// <summary>Grows every worker's shape buffer to at least this many shapes.</summary>
    IRenderer EnsureParallelCapacity(int capacityPerThread);
    /// <summary>Grows the worker buffers to fit this many entities spread across all threads.</summary>
    IRenderer EnsureParallelCapacityForEntities(int totalEntities);
    /// <summary>Returns a worker's shape buffer for direct writes; report how many you wrote with <c>SetParallelCount</c>.</summary>
    Shape[] GetParallelBuffer(int threadIndex);
    /// <summary>Returns a worker's depth buffer, written alongside its shape buffer for sorting.</summary>
    float[] GetParallelZBuffer(int threadIndex);
    /// <summary>Records how many shapes a worker wrote directly into its buffer.</summary>
    void SetParallelCount(int threadIndex, int count);
    /// <summary>Adds a shape from inside a parallel query. Each worker writes only to its own buffer, so no locking is needed.</summary>
    /// <example>
    /// The following example shows how to emit a circle for every entity in parallel and draw them together:
    /// <code>
    /// Renderer.InitializeParallelShapes(ECS.ThreadCount);
    ///
    /// ECS.Query&lt;Transform2D, Circle2D&gt;((int thread, int entity, ref Transform2D transform, ref Circle2D circle) =>
    ///     Renderer.DrawShapeParallel(thread, Shape.Circle(transform.Position, circle.Radius, Color.White)));
    ///
    /// Renderer.CollectParallelShapes().FlushShapes(target);
    /// </code>
    /// </example>
    void DrawShapeParallel(int threadIndex, Shape shape);
    /// <summary>Merges every worker buffer into the batch in thread order. Call after the parallel work finishes.</summary>
    IRenderer CollectParallelShapes();
    /// <summary>Empties every worker buffer without drawing.</summary>
    IRenderer ClearParallelShapes();
    /// <summary>Sorts one worker's shapes by depth, before <c>CollectParallelShapesSorted</c>.</summary>
    void SortParallelBufferByZ(int threadIndex);
    /// <summary>Merges the sorted worker buffers into the batch in depth order.</summary>
    IRenderer CollectParallelShapesSorted();
    /// <summary>Remembers the current render targets so you can draw elsewhere and come back with <c>PopTargets</c>.</summary>
    IRenderer PushTargets();
    /// <summary>Restores the render targets saved by the last <c>PushTargets</c>.</summary>
    IRenderer PopTargets();
    /// <summary>Directs the next draws into a render target. Pass null to draw to the scene surface.</summary>
    IRenderer SetTarget(RenderTarget2D target);
    /// <summary>Draws into two targets at once (one shader output each).</summary>
    IRenderer SetTargets(RenderTarget2D target0, RenderTarget2D target1);
    /// <summary>Draws into three targets at once (one shader output each).</summary>
    IRenderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2);
    /// <summary>Draws into four targets at once (one shader output each).</summary>
    IRenderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2, RenderTarget2D target3);
    /// <summary>Draws into up to four targets at once (one shader output each).</summary>
    IRenderer SetTargets(params RenderTarget2D[] targets);
    /// <summary>Fills the current targets with a color, transparent black by default.</summary>
    IRenderer Clear(Color? color = null);
    /// <summary>Draws colored triangles from a vertex list (three vertices per triangle) with the active shader.</summary>
    IRenderer DrawTriangles(ColorVertex[] vertices);
    /// <summary>Maps world coordinates to window pixels. Pass it to <c>BeginDraw</c> to draw sprites in world space.</summary>
    Matrix WorldTransform { get; }
    /// <summary>
    /// Centers a world area in the window and scales it to fit, keeping its aspect ratio. The fit is reapplied
    /// whenever the window is resized.
    /// </summary>
    /// <example>
    /// The following example shows how to frame a 1600 × 900 level centered on the origin:
    /// <code>
    /// Renderer.FitWorld(Vector2.Zero, new Vector2(1600, 900));
    /// </code>
    /// </example>
    void FitWorld(Vector2 Center, Vector2 Size);
    /// <summary>Returns the area set by <c>FitWorld</c>, or false when the default full-window view is in use.</summary>
    bool TryGetWorldFit(out Vector2 Center, out Vector2 Size);
}
