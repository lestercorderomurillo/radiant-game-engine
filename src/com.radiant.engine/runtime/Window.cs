using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.runtime;

public class Window : Game
{
    public static int BuildNumber { get; private set; }
    public static string BuildTag { get; private set; }

    public GraphicsDeviceManager GraphicsDeviceManager;

    public GameLoop GameLoop;

    public SpriteBatch SpriteBatch;

    public bool ResizePending { get; private set; }

    public Window(GameLoop gameLoop, WindowOptions Options = null)
    {
        Options ??= new WindowOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Options.Height);
        BuildNumber = typeof(Window).Assembly.GetName().Version?.Build ?? 0;
        BuildTag = $"Radiant {typeof(Window).Assembly.GetName().Version}";

        GraphicsDeviceManager = new GraphicsDeviceManager(this);

        Window.AllowUserResizing = true;
        Window.IsBorderless = false;
        Window.Title = Options.Title;

        IsMouseVisible = true;
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;

        GraphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.None;
        GraphicsDeviceManager.PreferredBackBufferWidth = Options.Width;
        GraphicsDeviceManager.PreferredBackBufferHeight = Options.Height;
        GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.HiDef;
        GraphicsDeviceManager.IsFullScreen = false;
        GraphicsDeviceManager.HardwareModeSwitch = true;
        GraphicsDeviceManager.PreferMultiSampling = false;

        GameLoop = gameLoop;
        Content.RootDirectory = "Content";
    }

    protected override void Initialize()
    {
        base.Initialize();

        // The native Vulkan swapchain can be created before the window reaches
        // its requested size. Reapply against the actual client area before drawing.
        GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
        GraphicsDeviceManager.ApplyChanges();

        SpriteBatch = new SpriteBatch(GraphicsDevice);
        GameLoop.SpriteBatch = SpriteBatch;

        Window.ClientSizeChanged += OnClientSizeChanged;
        GraphicsDevice.DeviceReset += OnClientSizeChanged;

        GameLoop.Initialize(this);
    }

    private void OnClientSizeChanged(object Sender, EventArgs Event)
    {
        if (Window.ClientBounds.Width > 0 && Window.ClientBounds.Height > 0)
            ResizePending = true;
    }

    public void ClearResizePending()
    {
        ResizePending = false;
    }

    public Vector2 GetScreenCenter()
    {
        return new Vector2(GraphicsDeviceManager.PreferredBackBufferWidth / 2, GraphicsDeviceManager.PreferredBackBufferHeight / 2);
    }

    public Vector2 GetScreenSize()
    {
        return new Vector2(GraphicsDeviceManager.PreferredBackBufferWidth, GraphicsDeviceManager.PreferredBackBufferHeight);
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        GameLoop.GameTime = gameTime;
        GameLoop.Update();

        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();
    }

    protected override void Draw(GameTime gameTime)
    {
        GameLoop.GameTime = gameTime;
        GameLoop.Render();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Window.ClientSizeChanged -= OnClientSizeChanged;
            if (GraphicsDevice != null) GraphicsDevice.DeviceReset -= OnClientSizeChanged;
            GameLoop.Dispose();
            SpriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }
}
