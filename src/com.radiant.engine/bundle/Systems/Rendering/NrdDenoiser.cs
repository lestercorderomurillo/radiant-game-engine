using System;
using System.Collections.Generic;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Official NRD diffuse REBLUR/RELAX using current hardware-traced signals.</summary>
internal sealed class NrdDenoiser(PathTracer Source, int Mode) : IDenoisingBackend
{
    private ITraceDenoiserSession Session;
    private IPathTraceSession PreviousTrace;
    private long PreviousRevision = -1;
    private bool Reset = true;
    private Texture2D Fallback;

    public string Name => Mode == 1 ? "NRD REBLUR" : "NRD RELAX";

    public bool Available
    {
        get
        {
            if (Source?.Renderer == null) { Status = "Assign an initialized Denoising.TraceSource."; return false; }
            if (Source.Renderer.SupportsDenoising(out var reason)) return true;

            Status = reason;

            return false;
        }
    }

    public string Status { get; private set; } = "Waiting for native tracing signals";
    public IReadOnlyDictionary<string, Func<Texture2D>> Buffers { get; } = new Dictionary<string, Func<Texture2D>>();

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public Texture2D GetOutput() => Session?.Output ?? Fallback;

    /// <summary>Discards accumulated image history so the next frame starts fresh.</summary>
    public void ResetHistory() => Reset = true;

    /// <summary>Processes native diffuse signals with the selected NRD backend.</summary>
    public void Render(in DenoisingFrame Frame)
    {
        if (!ReferenceEquals(Source.Renderer, Frame.Renderer)) throw new InvalidOperationException("NRD and its trace source must use the same renderer.");

        Fallback = Frame.Input;

        var trace = Source.Hardware;

        if (trace == null || !trace.DenoisingSignals || Source.NativeDenoiser != Mode || !Source.Geometry.HasCamera)
        {
            Session?.Dispose();
            Session = null;
            PreviousTrace = null;
            Reset = true;
            Status = "NRD requires active Vulkan tracing and a camera; forwarding raw radiance";

            return;
        }

        if (Session?.Output.Width != trace.Sample.Width || Session.Output.Height != trace.Sample.Height)
        {
            Session?.Dispose();
            Session = Frame.Renderer.CreateDenoiser(trace, Mode);
            Reset = true;
        }

        Reset |= PreviousRevision != Source.RadianceRevision || !ReferenceEquals(PreviousTrace, trace);
        Session.Render(trace, Source.Geometry.Camera, Reset);
        PreviousTrace = trace;
        PreviousRevision = Source.RadianceRevision;
        Reset = false;
        Status = $"{Name}: native Vulkan diffuse reconstruction";
    }

    /// <summary>Releases native NRD resources and invalidates their history.</summary>
    public void Dispose() { Session?.Dispose(); Session = null; PreviousTrace = null; Fallback = null; }
}
