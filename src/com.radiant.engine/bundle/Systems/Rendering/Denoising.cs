using System;
using System.Collections.Generic;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Built-in spatial, temporal, and native denoising choices.</summary>
public enum DenoiserMode { None, Bilateral, Atrous, Svgf, OpenImageDenoise, External, NrdReblur, NrdRelax }
/// <summary>Stable backend identifier and runtime capability information shown in settings.</summary>
public readonly record struct DenoiserOption(string Id, string Name, bool Available, string Reason);

/// <summary>Per-view HDR denoising with spatial, temporal and optional neural backends. Raw tracer accumulation stays independent.</summary>
[SystemTag("Rendering", "Denoising")]
public class Denoising : ImageSystem
{
    public DenoiserMode Mode;
    /// <summary>Optional native trace signal source for NRD diffuse reconstruction.</summary>
    public PathTracer TraceSource;
    public int Iterations = 3;
    public float Strength = 2;
    public float DepthSigma = 0.01f;
    public float NormalPower = 64;
    public float AlbedoSigma = 0.1f;
    public Func<Texture2D> Albedo;
    public Func<Texture2D> Normals;
    public Func<Texture2D> Depth;
    public Func<Texture2D> Emission;
    public Func<Texture2D> SingleSample;
    public Func<TraceCamera?> Camera;
    public Func<long> Revision;
    public OidnQuality NeuralQuality = OidnQuality.High;
    public OidnDevice NeuralDevice = OidnDevice.Auto;
    public int NeuralUpdateInterval = 8;

    private RenderTarget2D Intermediate;
    private IDenoisingBackend Backend;
    private string ActiveBackendId, ExternalId;
    private Func<int> NativeSignalProvider;
    private Dictionary<string, (Func<IDenoisingBackend> Factory, DenoiserOption Info)> Extensions;
    private IReadOnlyList<DenoiserOption> Options;

    public int OptionsRevision { get; private set; }
    public string BackendId => Mode switch { DenoiserMode.None => "none", DenoiserMode.Bilateral => "bilateral", DenoiserMode.Atrous => "atrous", DenoiserMode.Svgf => "svgf", DenoiserMode.OpenImageDenoise => "oidn", DenoiserMode.NrdReblur => "nrd.reblur", DenoiserMode.NrdRelax => "nrd.relax", _ => ExternalId };
    public string Status => Backend?.Status ?? (Mode == DenoiserMode.None ? "Raw radiance" : Mode == DenoiserMode.Bilateral ? "Joint bilateral: one spatial pass" : $"À-trous: {Iterations} spatial passes");
    public int TemporalFrames => (Backend as SvgfDenoiser)?.HistoryFrames ?? 0;
    public bool NeuralBusy => (Backend as OidnDenoiser)?.IsProcessing ?? false;
    public int NeuralJobs => (Backend as OidnDenoiser)?.CompletedJobs ?? 0;

    /// <summary>Returns the cached list of built-in and registered denoising capabilities.</summary>
    public IReadOnlyList<DenoiserOption> GetOptions()
    {
        if (Options != null) return Options;

        var result = new List<DenoiserOption>
        {
            new("none", "None", true, "Raw path-traced radiance"), new("bilateral", "Joint bilateral", true, "Portable spatial GPU filter"), new("atrous", "À-trous", true, "Portable multiscale GPU filter"),
            new("svgf", "SVGF (temporal diffuse)", true, "Requires current samples, guides and camera"), new("oidn", "OIDN neural", OidnNative.TryGet(out _), OidnNative.LoadError)
        };
        if (Extensions != null) foreach (var item in Extensions) result.Add(item.Value.Info);

        string nrdReason = "Assign Denoising.TraceSource to enable native tracing signals.";
        bool nrdAvailable = TraceSource != null && Renderer != null && Renderer.SupportsNrdDenoising(out nrdReason);
        if (!result.Exists(option => option.Id == "nrd.reblur")) result.Add(new("nrd.reblur", "NRD REBLUR", nrdAvailable, nrdReason));
        if (!result.Exists(option => option.Id == "nrd.relax")) result.Add(new("nrd.relax", "NRD RELAX", nrdAvailable, nrdReason));
        AddUnavailable(result, "optix", "OptiX AI", "Requires NVIDIA CUDA/OptiX and a native backend; not integrated.");
        AddUnavailable(result, "dlss.rr", "DLSS Ray Reconstruction", "Requires supported RTX hardware and native DLSS integration; not integrated.");
        AddUnavailable(result, "fsr.rr", "FSR Ray Regeneration", "FSR Ray Regeneration requires Windows 11 / DirectX 12; this renderer uses Vulkan. No FSR RR backend is integrated.");

        return Options = result.AsReadOnly();
    }

    /// <summary>Adds a backend option with an explanation of its missing runtime support.</summary>
    private static void AddUnavailable(List<DenoiserOption> options, string id, string name, string reason) { foreach (var option in options) if (option.Id == id) return; options.Add(new(id, name, false, reason)); }

    /// <summary>Register a fresh per-activation backend factory. No process-global renderer or backend state.</summary>
    public void RegisterBackend(string id, Func<IDenoisingBackend> factory)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "none" or "bilateral" or "atrous" or "svgf" or "oidn") throw new ArgumentException("Use a distinct backend ID.", nameof(id));

        ArgumentNullException.ThrowIfNull(factory);

        if (Extensions?.ContainsKey(id) == true) throw new ArgumentException("A backend with this ID is already registered.", nameof(id));

        using var probe = factory();
        Extensions ??= new();
        Extensions[id] = (factory, new(id, probe.Name, probe.Available, probe.Status));
        Options = null;
        OptionsRevision++;
    }

    /// <summary>Selects an available denoising backend by stable identifier.</summary>
    public bool TrySelectBackend(string id, out string reason)
    {
        foreach (var option in GetOptions())
        {
            if (option.Id != id) continue;

            reason = option.Reason;

            if (!option.Available) return false;

            Mode = id switch { "none" => DenoiserMode.None, "bilateral" => DenoiserMode.Bilateral, "atrous" => DenoiserMode.Atrous, "svgf" => DenoiserMode.Svgf, "oidn" => DenoiserMode.OpenImageDenoise, "nrd.reblur" when Extensions?.ContainsKey(id) != true => DenoiserMode.NrdReblur, "nrd.relax" when Extensions?.ContainsKey(id) != true => DenoiserMode.NrdRelax, _ => DenoiserMode.External };
            if (Mode == DenoiserMode.External) ExternalId = id;
            ConfigureNativeSignals();

            return true;
        }

        reason = "Unknown denoiser backend.";
        return false;
    }

    /// <summary>Discards accumulated image history so the next frame starts fresh.</summary>
    public void ResetHistory() => Backend?.ResetHistory();

    /// <summary>Returns a live diagnostic buffer exposed by the active denoising backend.</summary>
    private Texture2D BackendBuffer(string name) => Backend != null && Backend.Buffers.TryGetValue(name, out var get) ? get() : null;

    /// <summary>Creates the selected backend when its mode or registration changes.</summary>
    private IDenoisingBackend EnsureBackend()
    {
        string id = BackendId;

        if (Backend == null || ActiveBackendId != id)
        {
            Backend?.Dispose();
            ActiveBackendId = id;
            Backend = Mode switch { DenoiserMode.NrdReblur => new NrdDenoiser(TraceSource, 1), DenoiserMode.NrdRelax => new NrdDenoiser(TraceSource, 2), DenoiserMode.Svgf => new SvgfDenoiser(), DenoiserMode.OpenImageDenoise => new OidnDenoiser(), DenoiserMode.External when Extensions != null && Extensions.TryGetValue(id, out var entry) => entry.Factory(), _ => throw new InvalidOperationException("No denoising backend registered.") };
        }

        foreach (string name in Backend.Buffers.Keys) if (!BufferProviders.ContainsKey(name)) { string key = name; BufferProviders[key] = () => BackendBuffer(key); }

        if (Backend is OidnDenoiser neural) { neural.Quality = NeuralQuality; neural.Device = NeuralDevice; neural.UpdateInterval = NeuralUpdateInterval; }

        return Backend;
    }

    /// <summary>Collects current radiance, guides, and settings into a denoising frame.</summary>
    private DenoisingFrame Frame(Texture2D input) => new(Renderer, input, SingleSample?.Invoke(), Albedo?.Invoke(), Normals?.Invoke(), Depth?.Invoke(), Emission?.Invoke(), Camera?.Invoke(), Revision?.Invoke() ?? 0, Iterations, Strength, DepthSigma, NormalPower, AlbedoSigma);

    /// <summary>Requests the native tracing signals required by the selected denoiser.</summary>
    private void ConfigureNativeSignals()
    {
        if (TraceSource != null) TraceSource.NativeDenoiserProvider = NativeSignalProvider ??= () => Enabled ? Mode switch { DenoiserMode.NrdReblur => 1, DenoiserMode.NrdRelax => 2, _ => 0 } : 0;
    }

    /// <inheritdoc/>
    public override void Initialize() { Options = null; OptionsRevision++; ConfigureNativeSignals(); }

    /// <inheritdoc/>
    public override void Update()
    {
        ConfigureNativeSignals();

        if (!Enabled || Mode is DenoiserMode.None or DenoiserMode.Bilateral or DenoiserMode.Atrous or DenoiserMode.NrdReblur or DenoiserMode.NrdRelax || Input?.Invoke() is not { IsDisposed: false } input) return;

        var frame = Frame(input);
        if (frame.Albedo is { IsDisposed: false } && frame.Normals is { IsDisposed: false } && frame.Depth is { IsDisposed: false } && frame.Albedo.Width == input.Width && frame.Albedo.Height == input.Height && frame.Normals.Width == input.Width && frame.Normals.Height == input.Height && frame.Depth.Width == input.Width && frame.Depth.Height == input.Height) EnsureBackend().Update(frame);
    }

    /// <summary>Registers denoising inputs and intermediates for buffer inspection.</summary>
    public Denoising()
    {
        BufferProviders["Intermediate"] = () => Intermediate;

        foreach (string name in new[] { "Temporal illumination", "Luminance moments", "First-level history", "Surface history", "Wavelet intermediate", "Neural output" }) { string key = name; BufferProviders[key] = () => BackendBuffer(key); }
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public override Texture2D GetOutput() => !Enabled || Mode == DenoiserMode.None ? Input?.Invoke() : Mode is DenoiserMode.Bilateral or DenoiserMode.Atrous ? base.GetOutput() : Backend?.GetOutput() ?? Input?.Invoke();

    /// <inheritdoc/>
    public override void Render()
    {
        var input = Input?.Invoke();

        if (input == null || Mode == DenoiserMode.None) { ReleaseTargets(); ReleaseBackend(); return; }
        if (Mode is DenoiserMode.NrdReblur or DenoiserMode.NrdRelax)
        {
            ReleaseTargets();

            var native = EnsureBackend();

            if (!native.Available) throw new NotSupportedException(native.Status);

            native.Render(Frame(input));

            return;
        }
        if (Iterations < 1 || Iterations > 5) throw new ArgumentOutOfRangeException(nameof(Iterations), "Use 1 to 5 wavelet iterations.");
        if (!float.IsFinite(Strength) || Strength <= 0 || !float.IsFinite(DepthSigma) || DepthSigma <= 0 || !float.IsFinite(NormalPower) || NormalPower <= 0 || !float.IsFinite(AlbedoSigma) || AlbedoSigma <= 0) throw new ArgumentOutOfRangeException(nameof(Strength), "Denoising weights must be finite and positive.");

        var albedo = Albedo?.Invoke();
        var normals = Normals?.Invoke();
        var depth = Depth?.Invoke();
        var emission = Emission?.Invoke();
        bool Matches(Texture2D texture) => texture is { IsDisposed: false } && texture.Width == input.Width && texture.Height == input.Height;

        if (!Matches(albedo) || !Matches(normals) || !Matches(depth) || emission != null && !Matches(emission)) throw new InvalidOperationException("Denoising requires matching albedo, normal and linear-depth buffers; optional emission must match too.");
        if (Mode is not DenoiserMode.Bilateral and not DenoiserMode.Atrous)
        {
            ReleaseTargets();

            var backend = EnsureBackend();

            if (!backend.Available) throw new NotSupportedException(backend.Status);

            backend.Render(Frame(input));

            return;
        }

        ReleaseBackend();
        EnsureOutput(input, SurfaceFormat.Vector4);

        int passes = Mode == DenoiserMode.Atrous ? Iterations : 1;

        if (passes > 1)
        {
            if (Intermediate?.Width != input.Width || Intermediate.Height != input.Height)
            {
                Renderer.SetTarget(null);
                Intermediate?.Dispose();
                Intermediate = Renderer.CreateRenderTarget(input.Width, input.Height, SurfaceFormat.Vector4);
            }
        }
        else if (Intermediate != null) { Renderer.SetTarget(null); Intermediate.Dispose(); Intermediate = null; }

        var current = input;

        for (int pass = 0; pass < passes; pass++)
        {
            var target = (passes - pass) % 2 == 1 ? Output : Intermediate;
            Renderer.Reset().SetShader("Denoising").SetTechnique(Mode == DenoiserMode.Bilateral ? "Bilateral" : "Atrous").Configure(SamplerState.PointClamp, 0).SetTarget(target).SetParameter("Source", current).SetParameter("Albedo", albedo).SetParameter("Normals", normals).SetParameter("Depth", depth).SetParameter("Emission", emission ?? albedo).SetParameter("HasEmission", emission != null).SetParameter("TexelSize", new Vector2(1f / input.Width, 1f / input.Height)).SetParameter("StepWidth", 1 << pass).SetParameter("ColorSigma", Strength / (1 << pass)).SetParameter("DepthSigma", DepthSigma).SetParameter("NormalPower", NormalPower).SetParameter("AlbedoSigma", AlbedoSigma).Draw().Commit().SetTarget(null);
            current = target;
        }
    }

    /// <summary>Releases intermediate image targets owned by this stage.</summary>
    private void ReleaseTargets()
    {
        if (Output == null && Intermediate == null) return;

        Renderer.SetTarget(null);
        Output?.Dispose();
        Intermediate?.Dispose();
        Output = Intermediate = null;
    }

    /// <summary>Disposes the active backend and clears its cached references.</summary>
    private void ReleaseBackend() { if (Backend == null) return; Renderer.SetTarget(null); Backend.Dispose(); Backend = null; ActiveBackendId = null; }

    /// <inheritdoc/>
    public override void Dispose() { if (TraceSource != null) { TraceSource.NativeDenoiser = 0; TraceSource.NativeDenoiserProvider = null; } ReleaseTargets(); ReleaseBackend(); }
}
