using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using com.radiant.engine.input;

namespace com.radiant.engine.runtime;

/// <summary>Hosts the game loop in a MonoGame window and forwards resizing and drawing events.</summary>
public class Window : Game, IRuntimeHost
{
    public static int BuildNumber { get; private set; }
    public static string BuildTag { get; private set; }

    public GraphicsDeviceManager GraphicsDeviceManager;

    public GameLoop GameLoop;

    public bool ResizePending { get; private set; }

    private bool MaximizeOnShow;
    private MonoGameInputSource DesktopInput;
    private readonly WindowOptions Options;
    private StartupSession Startup;
    private StartupScreen LoadingScreen;
    private bool LoadingFramePresented;
    private bool MainWindowRestored;
    private bool AboutVisible;
    private bool AboutMouseDown;
    private bool SuppressEscape;
    private bool WaitForAboutRelease;
    private Color AboutCloseColor, AboutCloseHover, AboutCloseText;
    /// <summary>Whether the branded About panel is currently open.</summary>
    public bool IsAboutVisible => AboutVisible;
    /// <summary>True until application preparation and initial scene loading have completed.</summary>
    public bool IsLoading => Startup is { Finished: false };
    /// <summary>The current startup/update status, or null after startup resources have been released.</summary>
    public LoadingProgress LoadingProgress => Startup?.Progress;
    /// <summary>Failure that prevented startup; the loader stays visible until the window closes.</summary>
    public Exception StartupError => Startup?.Error;
    IInputSource IRuntimeHost.Input => DesktopInput ?? (IInputSource)NullInputSource.Instance;

    /// <summary>Creates a MonoGame host with the supplied game loop and window settings.</summary>
    public Window(GameLoop gameLoop, WindowOptions Options = null)
    {
        Options ??= new WindowOptions();
        this.Options = Options;
        MaximizeOnShow = Options.Maximized;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Options.Height);
        BuildNumber = typeof(Window).Assembly.GetName().Version?.Build ?? 0;
        BuildTag = $"Radiant {typeof(Window).Assembly.GetName().Version}";

        GraphicsDeviceManager = new GraphicsDeviceManager(this);

        Window.AllowUserResizing = !Options.ShowLoader;
        Window.IsBorderless = Options.ShowLoader;
        Window.Title = Options.Title;

        IsMouseVisible = true;
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;

        GraphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.None;
        GraphicsDeviceManager.PreferredBackBufferWidth = Options.ShowLoader ? 768 : Options.Width;
        GraphicsDeviceManager.PreferredBackBufferHeight = Options.ShowLoader ? 512 : Options.Height;
        GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.HiDef;
        GraphicsDeviceManager.IsFullScreen = false;
        GraphicsDeviceManager.HardwareModeSwitch = true;
        GraphicsDeviceManager.PreferMultiSampling = false;

        GameLoop = gameLoop;
        Content.RootDirectory = "Content";
    }

    void IRuntimeHost.PrepareScene(com.radiant.engine.core.Scene Scene) => Scene.Renderer = new com.radiant.engine.core.Renderer(this);
    void IRuntimeHost.ReleaseScene(com.radiant.engine.core.Scene Scene)
    {
        Scene.Renderer?.Dispose();
        Scene.Renderer = null;
    }
    void IRuntimeHost.BeginFrame(com.radiant.engine.core.Scene Scene) => Scene.Renderer.ClearBackBuffer(Color.Black);
    void IRuntimeHost.EndFrame(com.radiant.engine.core.Scene Scene) => Scene.Renderer.PresentToBackBuffer();

    /// <inheritdoc/>
    protected override void Initialize()
    {
        base.Initialize();

        // The native Vulkan swapchain can be created before the window reaches
        // its requested size. Reapply against the actual client area before drawing.
        GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
        GraphicsDeviceManager.ApplyChanges();

        Window.ClientSizeChanged += OnClientSizeChanged;
        GraphicsDevice.DeviceReset += OnClientSizeChanged;

        DesktopInput = new MonoGameInputSource(Window, () => IsActive);
        GameLoop.Initialize(this);
        Startup = new StartupSession(GameLoop, Options);
        if (Options.ShowLoader) LoadingScreen = new StartupScreen(GraphicsDevice);
    }

    /// <summary>Opens the shared Radiant artwork as a modal About panel using the supplied theme colors.</summary>
    public void ShowAbout(Color CloseColor, Color CloseHover, Color CloseText)
    {
        if (Startup != null) return;
        LoadingScreen ??= new StartupScreen(GraphicsDevice);
        AboutCloseColor = CloseColor;
        AboutCloseHover = CloseHover;
        AboutCloseText = CloseText;
        AboutMouseDown = Microsoft.Xna.Framework.Input.Mouse.GetState().LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        AboutVisible = true;
    }

    /// <summary>Closes the About panel without closing the application or advancing simulation time.</summary>
    public void CloseAbout()
    {
        AboutVisible = false;
        SuppressEscape = true;
        WaitForAboutRelease = true;
        GameLoop.ResetStartupTiming();
    }

    internal void HandleAboutInput(int X, int Y, bool Pressed, bool Escape)
    {
        if (!AboutVisible) return;
        var Bounds = StartupScreen.AboutBounds(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
        if (Escape || (Pressed && !AboutMouseDown && StartupScreen.AboutCloseBounds(Bounds).Contains(X, Y))) CloseAbout();
        AboutMouseDown = Pressed;
    }

    private void RestoreMainWindow()
    {
        if (MainWindowRestored) return;
        MainWindowRestored = true;
        Window.IsBorderless = false;
        Window.AllowUserResizing = true;
        GraphicsDeviceManager.PreferredBackBufferWidth = Options.Width;
        GraphicsDeviceManager.PreferredBackBufferHeight = Options.Height;
        GraphicsDeviceManager.ApplyChanges();
        if (MaximizeOnShow) { NativeWindow.Maximize(Window.Handle); MaximizeOnShow = false; }
    }

    /// <summary>Marks a window resize for deferred renderer updates.</summary>
    private void OnClientSizeChanged(object Sender, EventArgs Event)
    {
        if (Window.ClientBounds.Width > 0 && Window.ClientBounds.Height > 0) ResizePending = true;
    }

    /// <summary>Clears the deferred-resize flag once the new size has been handled.</summary>
    public void ClearResizePending()
    {
        ResizePending = false;
    }

    /// <summary>Returns the center of the current client area in pixels.</summary>
    public Vector2 GetScreenCenter()
    {
        return new Vector2(GraphicsDeviceManager.PreferredBackBufferWidth / 2, GraphicsDeviceManager.PreferredBackBufferHeight / 2);
    }

    /// <summary>Returns the current client-area dimensions in pixels.</summary>
    public Vector2 GetScreenSize()
    {
        return new Vector2(GraphicsDeviceManager.PreferredBackBufferWidth, GraphicsDeviceManager.PreferredBackBufferHeight);
    }

    /// <inheritdoc/>
    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (Startup != null)
        {
            if (Microsoft.Xna.Framework.Input.Keyboard.GetState().IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape))
            {
                Exit();
                return;
            }
            if (Options.ShowLoader && !LoadingFramePresented) return;
            LoadingFramePresented = false;
            do { Startup.Advance(); }
            while (!Options.ShowLoader && Startup.CanAdvanceSynchronously);
            if (Startup.Error != null && !Options.ShowLoader)
                throw new InvalidOperationException("Radiant startup failed.", Startup.Error);
            if (!Startup.Finished) return;
            Startup.Dispose();
            Startup = null;
            LoadingScreen?.Dispose();
            LoadingScreen = null;
            RestoreMainWindow();
            GameLoop.ResetStartupTiming();
        }

        if (AboutVisible)
        {
            var Mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
            bool Pressed = Mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
            bool Escape = Microsoft.Xna.Framework.Input.Keyboard.GetState().IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape);
            HandleAboutInput(Mouse.X, Mouse.Y, Pressed, Escape);
            return;
        }

        if (WaitForAboutRelease)
        {
            if (Microsoft.Xna.Framework.Input.Mouse.GetState().LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed ||
                Microsoft.Xna.Framework.Input.Keyboard.GetState().IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Escape)) return;
            WaitForAboutRelease = false;
            GameLoop.ResetStartupTiming();
        }

        GameLoop.Update();

        if (!GameLoop.Controls.IsDown(Key.Escape)) SuppressEscape = false;
        if (!SuppressEscape && GameLoop.Controls.IsDown(Key.Escape)) Exit();
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime gameTime)
    {
        if (Startup != null)
        {
            if (LoadingScreen != null) LoadingScreen.Draw(Startup.Progress, Startup.Error != null);
            else GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.Black);
            LoadingFramePresented = true;
            // Loading does not pass through the game loop's frame limiter.
            System.Threading.Thread.Sleep(8);
            return;
        }

        GameLoop.Render();
        if (AboutVisible)
        {
            var Bounds = StartupScreen.AboutBounds(GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);
            var Mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
            bool Hovered = StartupScreen.AboutCloseBounds(Bounds).Contains(Mouse.X, Mouse.Y);
            LoadingScreen.DrawAbout(Bounds, BuildTag, (Hovered ? AboutCloseHover : AboutCloseColor).ToNative(), AboutCloseText.ToNative());
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Startup?.Dispose();
            LoadingScreen?.Dispose();
            Window.ClientSizeChanged -= OnClientSizeChanged;
            if (GraphicsDevice != null) GraphicsDevice.DeviceReset -= OnClientSizeChanged;
            GameLoop.Dispose();
            DesktopInput?.Dispose();
        }

        base.Dispose(disposing);
    }
}
