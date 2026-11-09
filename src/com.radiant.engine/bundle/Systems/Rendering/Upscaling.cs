using System;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Selects a UDR reconstruction algorithm, independently of the resolution preset.</summary>
/// <remarks>Use <see cref="UDRQuality"/> to choose Performance, Balanced or Native resolution.
/// UDR1–UDR3 require HRC-derived input. UDR4 also supports generic 2D and 3D images through spatial reconstruction.</remarks>
public enum UpscalingMode
{
    /// <summary>Passes the input through without reconstruction.</summary>
    None,
    /// <summary>Spatial reconstruction using HRC geometry, without frame history.</summary>
    UDR1,
    /// <summary>Spatial reconstruction and temporal accumulation for HRC input.</summary>
    UDR2,
    /// <summary>Lanczos reconstruction, edge refinement and temporal accumulation for HRC input.</summary>
    UDR3,
    /// <summary>Temporal reconstruction for HRC input, or spatial reconstruction for generic images.</summary>
    UDR4
}

/// <summary>Reconstructs an image at display resolution using one selected UDR algorithm.</summary>
/// <remarks>
/// Connect <c>Input</c> to the preceding image stage and present <see cref="GetOutput"/> separately.
/// Set <see cref="HrcInput"/> before selecting a mode or initializing the system. HRC-derived images can use
/// UDR1–UDR4; other 2D and 3D images use UDR4's spatial fallback. The stage owns and disposes its selected algorithm.
/// </remarks>
/// <seealso cref="UpscalingMode"/>
/// <seealso cref="UDRQuality"/>
[SystemTag("Rendering", "Upscaling")]
public class Upscaling : ImageSystem
{
    /// <summary>Whether the input is derived from this scene's HRC lighting and matching geometry buffers.</summary>
    /// <remarks>Set before selecting a mode or initializing the system. Do not enable for unrelated raster or path-traced images.</remarks>
    public bool HrcInput;
    /// <summary>Gets the selected algorithm; use <see cref="Select"/> to change it. Defaults to <see cref="UpscalingMode.None"/>.</summary>
    public UpscalingMode Mode { get; private set; }
    /// <summary>Gets the per-axis render scale, or 1 when the stage is disabled or no algorithm is selected.</summary>
    public float Scale => !Enabled || Mode == UpscalingMode.None ? 1f : UDRQuality.ScaleNormalized;
    /// <summary>Describes whether reconstruction is bypassed, HRC-guided or using the generic spatial fallback.</summary>
    public string Status => Mode == UpscalingMode.None ? "Native / reconstruction off"
        : HrcInput ? "HRC geometry guidance; temporal history in UDR2–4"
        : "UDR4 spatial fallback; temporal guidance unavailable";

    private core.System Backend;
    private Func<Texture2D> BackendOutput;
    private bool Initialized;
    private bool ScalingView;
    private Point? OriginalResolution;

    /// <summary>Selects an algorithm and releases the previous algorithm's resources.</summary>
    /// <param name="mode">Reconstruction algorithm, or <see cref="UpscalingMode.None"/> to bypass the stage.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="UpscalingMode"/>.</exception>
    /// <exception cref="InvalidOperationException">UDR1, UDR2 or UDR3 is selected without <see cref="HrcInput"/>.</exception>
    /// <remarks>Algorithm selection and resolution quality are independent. Change quality through <see cref="UDRQuality.Set"/>.</remarks>
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

    /// <summary>Resolves a producer's view size using the active per-axis render scale.</summary>
    /// <param name="resolution">The producer's current explicit resolution, or null to use the window size.</param>
    /// <returns>The scaled view size, or the original explicit resolution when reconstruction is bypassed.</returns>
    /// <remarks>Call when updating the view. The first unscaled resolution is retained while scaling is active.</remarks>
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

    /// <summary>Discards reconstruction history after a camera cut, teleport or level change.</summary>
    /// <remarks>Resets UDR4 directly. Other active algorithms are recreated to clear accumulated state.</remarks>
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
