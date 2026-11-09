using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Available image reconstruction backends, including native resolution.</summary>
public enum UpscalingMode { None, UDR1, UDR2, UDR3, UDR4 }

/// <summary>Shared reconstruction stage. Owns only the selected backend and never presents implicitly.</summary>
[SystemTag("Rendering", "Upscaling")]
public class Upscaling : ImageSystem
{
    /// <summary>Enable HRC guidance only for an input produced by the scene's HRC pipeline.</summary>
    public bool HrcInput;
    public UpscalingMode Mode { get; private set; }
    public float Scale => !Enabled || Mode == UpscalingMode.None ? 1f : UDRQuality.ScaleNormalized;
    public string Status => Mode == UpscalingMode.None ? "Native / reconstruction off"
        : HrcInput ? "HRC geometry guidance; temporal history in UDR2–4"
        : "UDR4 spatial fallback; temporal guidance unavailable";

    private core.System Backend;
    private Func<Texture2D> BackendOutput;
    private bool Initialized;
    private bool ScalingView;
    private Point? OriginalResolution;

    /// <summary>Select a backend, releasing the previous backend's targets, windows, and subscriptions.</summary>
    public void Select(UpscalingMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!HrcInput && mode is UpscalingMode.UDR1 or UpscalingMode.UDR2 or UpscalingMode.UDR3)
            throw new InvalidOperationException("UDR1–3 require an HRC input. Use UDR4 for a generic image source.");
        if (Mode == mode && (mode == UpscalingMode.None || Backend != null || !Initialized)) return;

        ReleaseBackend();
        Mode = mode;

        if (!Initialized) return;
        if (mode == UpscalingMode.None) { Renderer.RenderScale = 1; return; }

        switch (mode)
        {
            case UpscalingMode.UDR1:
                var spatial = new UDR1();
                spatial.SetInputSource(() => Input?.Invoke());
                Backend = spatial;
                BackendOutput = spatial.GetOutput;
                break;
            case UpscalingMode.UDR2:
                var temporal = new UDR2();
                temporal.SetInputSource(() => Input?.Invoke());
                Backend = temporal;
                BackendOutput = temporal.GetOutput;
                break;
            case UpscalingMode.UDR3:
                var lanczos = new UDR3();
                lanczos.SetInputSource(() => Input?.Invoke());
                Backend = lanczos;
                BackendOutput = lanczos.GetOutput;
                break;
            case UpscalingMode.UDR4:
                var reconstruction = new UDR4 { Present = false, UseHrcGeometry = HrcInput, Temporal = HrcInput };
                reconstruction.SetInputSource(() => Input?.Invoke());
                Backend = reconstruction;
                BackendOutput = reconstruction.GetOutput;
                break;
        }

        Backend.Scene = Scene;
        Backend.Renderer = Renderer;
        Backend.Initialize();
    }

    /// <summary>Scale a scene view while retaining its explicit resolution when reconstruction is bypassed.</summary>
    public Point? ResolveViewSize(Point? resolution)
    {
        if (Scale == 1)
        {
            if (!ScalingView) return resolution;

            ScalingView = false;
            return OriginalResolution;
        }

        if (!ScalingView) { OriginalResolution = resolution; ScalingView = true; }

        var size = OriginalResolution ?? new Point(Renderer.ScreenWidth, Renderer.ScreenHeight);

        return new Point(Math.Max(1, (int)(size.X * Scale)), Math.Max(1, (int)(size.Y * Scale)));
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        Initialized = true;
        Select(Mode);
    }

    /// <summary>Invalidate reconstruction on scene cuts. Legacy backends are recreated to discard history.</summary>
    public void ResetHistory()
    {
        if (Backend is UDR4 reconstruction) reconstruction.ResetHistory();
        else if (Backend != null) { var mode = Mode; ReleaseBackend(); Select(mode); }
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public override Texture2D GetOutput() => Enabled && Mode != UpscalingMode.None && UDRQuality.ScaleFactor != 100
        ? BackendOutput?.Invoke() ?? Input?.Invoke() : Input?.Invoke();

    /// <inheritdoc/>
    public override void Render()
    {
        if (Input?.Invoke() == null || Mode == UpscalingMode.None || UDRQuality.ScaleFactor == 100) return;

        // Both Update-phase HRC and Render-phase image producers have completed by this stage.
        Backend?.Update();
    }

    /// <inheritdoc/>
    public override void OnResize() => Backend?.OnResize();

    /// <summary>Disposes the active backend and clears its cached references.</summary>
    private void ReleaseBackend()
    {
        Backend?.Dispose();
        Backend = null;
        BackendOutput = null;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        ReleaseBackend();
        Initialized = false;
        base.Dispose();
    }
}
