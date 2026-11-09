using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

internal sealed class StartupRegressionWindow : Window
{
    private readonly string Mode;
    private readonly ProbeScene Scene = new();
    private readonly Preparation Fixture;
    private int Frames;
    private int SceneFrames;
    private bool Captured;
    private bool SawReady;
    private RenderTarget2D CaptureTarget;
    private StartupScreen CaptureScreen;

    public StartupRegressionWindow(string Mode) : this(Mode, new Preparation(Mode)) { }

    private StartupRegressionWindow(string Mode, Preparation Fixture)
        : base(new GameLoop(), new WindowOptions
        {
            Width = 960, Height = 640, Title = "Radiant startup regression",
            ShowLoader = Mode != "hidden", PrepareApplication = Fixture.Run
        })
    {
        this.Mode = Mode;
        this.Fixture = Fixture;
        GameLoop.AddScene(Scene);
    }

    protected override void Draw(GameTime Time)
    {
        base.Draw(Time);
        if (CaptureTarget != null) SaveCapture();
        Frames++;
        if (Frames > 600) throw new Exception("Startup did not finish");
        if (IsLoading)
        {
            if (Scene.Updates != 0) throw new Exception("Scene updated behind loader");
            if (Fixture.Started.Task.IsCompleted && Frames >= 4)
            {
                if (Mode == "cancel")
                {
                    Exit();
                    return;
                }
                Fixture.Continue.TrySetResult();
            }
            if (StartupError != null)
            {
                if (Mode != "failure" || Scene.Setups != 0) throw new Exception("Incorrect failure handling");
                Console.WriteLine("PASS: update failure stays visible and does not initialize the scene.");
                Exit();
                return;
            }
            if (!Captured && Mode == "success" && LoadingProgress.Completion == 0.35)
            {
                Capture();
                Captured = true;
                GraphicsDeviceManager.PreferredBackBufferWidth = 1024;
                GraphicsDeviceManager.PreferredBackBufferHeight = 640;
                GraphicsDeviceManager.ApplyChanges();
            }
            if (LoadingProgress.Completion == 1) SawReady = true;
            return;
        }
        if (++SceneFrames == 3)
        {
            if (Scene.Setups != 1 || Scene.Updates < 1) throw new Exception("Scene handoff failed");
            var Target = Scene.Renderer.SceneRT;
            var Pixels = new Color[Target.Width * Target.Height];
            Target.GetData(Pixels);
            if (Pixels.Count(Pixel => Pixel.R > 12 || Pixel.G > 12 || Pixel.B > 12) < 500)
                throw new Exception("Real lighting scene is blank after loader handoff");
            if (Mode == "success" && (!Captured || !SawReady)) throw new Exception("Startup skipped visible progress");
            Console.WriteLine($"PASS: {Mode} loader, real Vulkan drawing and lit scene handoff.");
            Exit();
        }
    }

    private void Capture()
    {
        CaptureScreen = new StartupScreen(GraphicsDevice);
        CaptureTarget = new RenderTarget2D(GraphicsDevice, 1536, 1024, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        CaptureScreen.Draw(LoadingProgress, false, CaptureTarget);
        GraphicsDevice.SetRenderTarget(null);
    }

    // Read after presentation: stock DesktopVK may return unsubmitted target contents within the same frame.
    private void SaveCapture()
    {
        var Texture = CaptureTarget;
        var Pixels = new Microsoft.Xna.Framework.Color[1536 * 1024];
        Texture.GetData(Pixels);
        Directory.CreateDirectory("artifacts/startup");
        using var Output = File.Create("artifacts/startup/loader.png");
        Texture.SaveAsPng(Output, 1536, 1024);
        if (Pixels[950 * 1536 + 80].R < 240 || Pixels[950 * 1536 + 1400].R > 100)
            throw new Exception($"Live progress bar mismatch: filled={Pixels[950 * 1536 + 80]}, empty={Pixels[950 * 1536 + 1400]}, status={LoadingProgress}");
        CaptureScreen.Dispose();
        CaptureScreen = null;
        CaptureTarget.Dispose();
        CaptureTarget = null;
    }

    protected override void Dispose(bool Disposing)
    {
        if (Disposing) { CaptureScreen?.Dispose(); CaptureTarget?.Dispose(); }
        base.Dispose(Disposing);
        if (Disposing && Mode == "cancel")
        {
            if (!Fixture.Cancelled.Task.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Closing did not cancel update work");
            Console.WriteLine("PASS: closing loader cancels pending update work.");
        }
    }

    private sealed class Preparation(string Mode)
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Run(IProgress<LoadingProgress> Progress, CancellationToken Token)
        {
            Progress.Report(new LoadingProgress("Checking for updates...", 0.35));
            Started.TrySetResult();
            try
            {
                await Continue.Task.WaitAsync(Token);
                if (Mode == "failure") throw new InvalidOperationException("Expected updater failure");
            }
            finally { if (Token.IsCancellationRequested) Cancelled.TrySetResult(); }
        }
    }

    private sealed class ProbeScene : SimpleLightScene
    {
        public int Setups, Updates;
        public override void SetupScene() { base.SetupScene(); Setups++; }
        public override void Update() => Updates++;
    }
}
