using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Quality presets passed to the Open Image Denoise backend.</summary>
public enum OidnQuality { Fast, Balanced, High }
/// <summary>Compute-device preferences for Open Image Denoise.</summary>
public enum OidnDevice { Auto, CPU, GPU }

/// <summary>Optional OIDN 2 neural backend. GPU access stays on the render thread; native inference runs on a worker.</summary>
public sealed class OidnDenoiser : IDenoisingBackend
{
    public string Name => "OIDN neural";
    public bool Available => OidnNative.TryGet(out _);
    public string Status { get; private set; } = "OIDN ready";
    public OidnQuality Quality = OidnQuality.High;
    public OidnDevice Device = OidnDevice.Auto;
    public int UpdateInterval = 8;
    public int CompletedJobs { get; private set; }
    public bool IsProcessing => Pending != null;
    public double InferenceMilliseconds { get; private set; }

    private Texture2D CurrentInput;
    private RenderTarget2D Output;
    private Texture2D NeuralImage;
    private OidnNative.Session Session;
    private HostImages Images;
    private Task<Result> Pending;
    private Key CurrentKey, LastRenderedKey;
    private bool HasKey, HasRendered, ValidOutput;
    private volatile bool Disposed;
    private int Generation, Updates, LastUpdate = -1000;
    private string Failure;
    private readonly Dictionary<string, Func<Texture2D>> Outputs;

    public IReadOnlyDictionary<string, Func<Texture2D>> Buffers => Outputs;

    /// <summary>Input identity used to reject stale asynchronous denoising results.</summary>
    private readonly record struct Key(int Width, int Height, TraceCamera? Camera, long Revision, OidnQuality Quality, OidnDevice Device);
    /// <summary>Completed denoised pixels paired with their originating input identity.</summary>
    private sealed record Result(Key Key, int Generation, Vector4[] Color, string Device, double Milliseconds);

    /// <summary>Registers asynchronous denoising output and diagnostic buffers.</summary>
    public OidnDenoiser() { Outputs = new() { ["Neural output"] = () => ValidOutput ? Output : null }; }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public Texture2D GetOutput() => ValidOutput ? Output : CurrentInput;

    /// <summary>Discards accumulated image history so the next frame starts fresh.</summary>
    public void ResetHistory() { Interlocked.Increment(ref Generation); ValidOutput = false; LastUpdate = -1000; Failure = null; Status = "OIDN waiting for current frame"; }

    /// <summary>Builds an identity for the current radiance, camera, and geometry revision.</summary>
    private Key FrameKey(in DenoisingFrame frame) => new(frame.Input.Width, frame.Input.Height, frame.Camera, frame.Revision, Quality, Device);

    /// <summary>Detects changed input identity and invalidates stale denoising work.</summary>
    private void Observe(in DenoisingFrame frame)
    {
        CurrentInput = frame.Input;

        var key = FrameKey(frame);

        if (!HasKey || key != CurrentKey) { ResetHistory(); CurrentKey = key; HasKey = true; }
    }

    /// <summary>Observes completed asynchronous filtering work before the next render pass.</summary>
    public void Update(in DenoisingFrame frame)
    {
        if (Disposed) return;

        Observe(frame);
        Updates++;

        if (!HasRendered || CurrentKey != LastRenderedKey || !frame.Camera.HasValue || Pending != null || Failure != null || Updates - LastUpdate < Math.Max(1, UpdateInterval)) return;
        if (!OidnNative.TryGet(out var api)) { Status = OidnNative.LoadError; return; }
        if (frame.Input.IsDisposed || frame.Albedo.IsDisposed || frame.Normals.IsDisposed) return;

        // Read only completed previous-frame images during Update, never invoke GPU APIs on the worker.
        int count = frame.Input.Width * frame.Input.Height;
        if (Images?.Pixels.Length != count) Images = new HostImages(count);

        var images = Images;
        images.Read(frame.Input, images.Pixels);
        images.Read(frame.Albedo, images.Albedo);
        images.Read(frame.Normals, images.Normals);

        Vector4[] emission = null;

        if (frame.Emission != null) { images.Emission ??= new Vector4[count]; images.Read(frame.Emission, images.Emission); emission = images.Emission; }

        var pixels = images.Pixels;
        var albedo = images.Albedo;
        var normals = images.Normals;
        var color = images.Color;
        var diffuse = images.Diffuse;
        var normal = images.Normal;

        for (int i = 0; i < color.Length; i++)
        {
            float alpha = Math.Max(pixels[i].W, 1e-6f);
            color[i] = Vector3.Max(new Vector3(pixels[i].X, pixels[i].Y, pixels[i].Z) / alpha, Vector3.Zero);
            diffuse[i] = Vector3.Clamp(new Vector3(albedo[i].X, albedo[i].Y, albedo[i].Z) / Math.Max(albedo[i].W, 1e-6f), Vector3.Zero, Vector3.One);
            normal[i] = normals[i].W > 0 ? Vector3.Clamp(new Vector3(normals[i].X, normals[i].Y, normals[i].Z) / normals[i].W * 2 - Vector3.One, -Vector3.One, Vector3.One) : Vector3.Zero;
        }

        var key = CurrentKey;
        int generation = Generation;
        LastUpdate = Updates;
        Status = $"OIDN {Quality}: processing asynchronously";
        Pending = Task.Run(() =>
        {
            if (Session == null || Session.Width != key.Width || Session.Height != key.Height || Session.Quality != key.Quality || Session.Preference != key.Device)
            {
                Session?.Dispose();
                Session = null;
                Session = new OidnNative.Session(api, key.Width, key.Height, key.Quality, key.Device);
            }

            var timer = global::System.Diagnostics.Stopwatch.StartNew();
            var denoised = Session.Denoise(color, diffuse, normal, () => Disposed || generation != Volatile.Read(ref Generation));
            timer.Stop();

            var result = images.Result;

            for (int i = 0; i < result.Length; i++)
            {
                // Preserve coverage, miss pixels and directly visible emitters exactly.
                bool preserve = pixels[i].W <= 0 || normals[i].W <= 0 || emission != null && emission[i].X + emission[i].Y + emission[i].Z > 0;
                var rgb = Vector3.Max(denoised[i], Vector3.Zero);

                if (!float.IsFinite(rgb.X + rgb.Y + rgb.Z)) throw new InvalidOperationException("OIDN returned non-finite HDR radiance.");

                result[i] = preserve ? pixels[i] : new Vector4(rgb * pixels[i].W, pixels[i].W);
            }

            return new Result(key, generation, result, Session.DeviceName.Trim(), timer.Elapsed.TotalMilliseconds);
        });
    }

    /// <summary>Schedules denoising or publishes the latest result matching the current frame.</summary>
    public void Render(in DenoisingFrame frame)
    {
        Observe(frame);
        LastRenderedKey = CurrentKey;
        HasRendered = true;
        if (Pending is { IsCompleted: true }) Publish(frame);

        if (!ValidOutput) return;

        // Composite against this frame, not the older worker snapshot: coverage, misses and emitters stay current.
        frame.Renderer.Reset().SetShader("Denoising").SetTechnique("NeuralComposite").Configure(SamplerState.PointClamp, 0).SetTarget(Output).SetParameter("Source", frame.Input).SetParameter("Filtered", NeuralImage).SetParameter("Normals", frame.Normals).SetParameter("Emission", frame.Emission ?? frame.Albedo).SetParameter("HasEmission", frame.Emission != null).Draw().Commit().SetTarget(null);
    }

    /// <summary>Uploads a completed result only when it still matches the current input.</summary>
    private void Publish(in DenoisingFrame frame)
    {
        var task = Pending;
        Pending = null;

        if (task.IsCanceled || task.IsFaulted && task.Exception.GetBaseException() is OperationCanceledException) { Status = "OIDN cancelled obsolete camera/scene work"; return; }
        if (task.IsFaulted) { Failure = task.Exception.GetBaseException().Message; Status = Failure; return; }

        var result = task.Result;

        if (result.Generation != Generation || result.Key != CurrentKey) { Status = "OIDN discarded obsolete camera/scene result"; return; }

        if (Output?.Width != frame.Input.Width || Output.Height != frame.Input.Height)
        {
            frame.Renderer.SetTarget(null);
            Output?.Dispose();
            NeuralImage?.Dispose();
            Output = frame.Renderer.CreateRenderTarget(frame.Input.Width, frame.Input.Height, SurfaceFormat.Vector4);
            NeuralImage = frame.Renderer.CreateTexture(frame.Input.Width, frame.Input.Height, SurfaceFormat.Vector4);
        }

        NeuralImage.SetData(result.Color);
        ValidOutput = true;
        CompletedJobs++;
        InferenceMilliseconds = result.Milliseconds;
        Status = $"OIDN {Quality} / {result.Device} / {InferenceMilliseconds:F1} ms inference";
    }

    /// <summary>A single outstanding job owns these arrays until publication/discard. Reuse avoids large per-job GC allocations.</summary>
    private sealed class HostImages
    {
        public readonly Vector4[] Pixels, Albedo, Normals, Result;
        public Vector4[] Emission;
        public readonly Vector3[] Color, Diffuse, Normal;

        private Color[] PackedColors;
        private Half[] PackedHalves;

        /// <summary>Allocates host radiance and guide storage for the requested image dimensions.</summary>
        public HostImages(int count)
        {
            Pixels = new Vector4[count];
            Albedo = new Vector4[count];
            Normals = new Vector4[count];
            Result = new Vector4[count];
            Color = new Vector3[count];
            Diffuse = new Vector3[count];
            Normal = new Vector3[count];
        }

        /// <summary>Reads a rendered pixel or image for the current regression assertion.</summary>
        public void Read(Texture2D texture, Vector4[] values)
        {
            if (texture.Format == SurfaceFormat.Vector4) { texture.GetData(values); return; }
            if (texture.Format == SurfaceFormat.HalfVector4)
            {
                PackedHalves ??= new Half[checked(values.Length * 4)];
                texture.GetData(PackedHalves);

                for (int i = 0; i < values.Length; i++) values[i] = new Vector4((float)PackedHalves[i * 4], (float)PackedHalves[i * 4 + 1], (float)PackedHalves[i * 4 + 2], (float)PackedHalves[i * 4 + 3]);

                return;
            }

            PackedColors ??= new Color[values.Length];
            texture.GetData(PackedColors);

            for (int i = 0; i < values.Length; i++) values[i] = PackedColors[i].ToVector4();
        }
    }

    /// <summary>Stops outstanding denoising work and releases host and renderer resources.</summary>
    public void Dispose()
    {
        Disposed = true;
        Interlocked.Increment(ref Generation);
        Output?.Dispose();
        NeuralImage?.Dispose();
        Output = null;
        NeuralImage = null;
        CurrentInput = null;
        Images = null;
        ValidOutput = false;
        if (Pending is { } task) task.ContinueWith(completed => { _ = completed.Exception; Session?.Dispose(); Session = null; }, TaskScheduler.Default);
        else { Session?.Dispose(); Session = null; }
    }
}
