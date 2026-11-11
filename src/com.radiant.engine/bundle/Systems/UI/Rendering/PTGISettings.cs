using System;
using System.Collections.Generic;
using System.Linq;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Optional per-view path tracing controls, shared by perspective and orthographic adapters.</summary>
[SystemTag("Rendering", "Tools")]
public class PTGISettings : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;
    public PTGI Source;
    public Denoiser Denoiser;
    public string Id = "pathTracing";
    public string Title = "Path tracing";
    public Action Changed;

    private IReadOnlyList<DenoiserOption> Options;
    private int OptionsRevision;
    private string SelectionError;
    private string TraceSelectionError;
    private string NeuralId => Id + "Neural";

    /// <summary>Finds the settings-list index for the selected backend identifier.</summary>
    private int SelectedIndex() { for (int i = 0; i < Options.Count; i++) if (Options[i].Id == Denoiser.BackendId) return i; return 0; }

    /// <summary>Builds display labels for available and unavailable rendering backends.</summary>
    private string[] OptionNames() => Options.Select(option => option.Available ? option.Name : option.Name + " (unavailable)").ToArray();

    /// <inheritdoc/>
    public override void Initialize()
    {
        if (Source == null) throw new InvalidOperationException("PTGISettings requires a PTGI source.");

        Inspector.CreateWindow(Id, Title, LayoutOrder: 200, Visible: false);
        Inspector.AddLabel(Id, "samples", "Path samples: 0");
        Inspector.AddDropdown(Id, "traceBackend", "Tracing backend", ["Auto", "GPU shader BVH", "Vulkan hardware RT"], (int)Source.Backend, index =>
        {
            if (index == (int)PTGIBackend.Vulkan && !Renderer.SupportsVulkanPathTracing(out var reason))
            {
                TraceSelectionError = reason;
                Inspector.SetDropdownValue(Id, "traceBackend", (int)Source.Backend);
                return;
            }

            TraceSelectionError = null;
            Source.Backend = (PTGIBackend)index;
            Source.ResetAccumulation();
            Changed?.Invoke();
        });
        Inspector.AddLabel(Id, "traceStatus", Source.BackendStatus);
        Inspector.AddSlider(Id, "bounces", "Path bounces", 1, 8, Source.MaxBounces, value => { Source.MaxBounces = (int)value; Changed?.Invoke(); });
        Inspector.AddSlider(Id, "traceScale", Source.SeparateLighting ? "Lighting resolution" : "Path resolution", 0.25f, 1, Source.ResolutionScale, value => { Source.ResolutionScale = value; Changed?.Invoke(); });

        if (Denoiser == null) return;

        Options = Denoiser.GetOptions();
        OptionsRevision = Denoiser.OptionsRevision;
        Inspector.AddDropdown(Id, "denoiser", "Denoiser", OptionNames(), SelectedIndex(), index =>
        {
            if (index < 0 || index >= Options.Count) return;

            if (Denoiser.TrySelectBackend(Options[index].Id, out var reason)) { SelectionError = null; Changed?.Invoke(); }
            else { SelectionError = reason; Inspector.SetDropdownValue(Id, "denoiser", SelectedIndex()); }
        });
        Inspector.AddLabel(Id, "denoiserStatus", "");
        Inspector.AddSlider(Id, "denoiseStrength", "Denoising strength", 0.1f, 8, Denoiser.Strength, value => { Denoiser.Strength = value; Changed?.Invoke(); });
        Inspector.AddSlider(Id, "denoiseIterations", "Wavelet iterations", 1, 5, Denoiser.Iterations, value => { Denoiser.Iterations = (int)value; Changed?.Invoke(); });
        Inspector.AddButton(Id, "resetDenoiser", "Reset denoising history", () => { Denoiser.ResetHistory(); Changed?.Invoke(); });
        Inspector.AddButton(Id, "neuralSettings", "Neural denoising settings", () => Inspector.ShowWindow(NeuralId));
        Inspector.CreateWindow(NeuralId, "Neural denoising", LayoutOrder: 195, Visible: false);
        Inspector.AddDropdown(NeuralId, "quality", "Quality", ["Fast", "Balanced", "High"], (int)Denoiser.NeuralQuality, index => { Denoiser.NeuralQuality = (OidnQuality)index; Changed?.Invoke(); });
        Inspector.AddDropdown(NeuralId, "device", "Device", ["Auto", "CPU", "GPU"], (int)Denoiser.NeuralDevice, index => { Denoiser.NeuralDevice = (OidnDevice)index; Changed?.Invoke(); });
        Inspector.AddSlider(NeuralId, "interval", "Update every N frames", 1, 32, Denoiser.NeuralUpdateInterval, value => Denoiser.NeuralUpdateInterval = (int)value);
        Inspector.AddLabel(NeuralId, "transfer", "Asynchronous inference; host image transfers");
        Inspector.AddLabel(NeuralId, "status", "");
    }

    /// <inheritdoc/>
    public override void Update()
    {
        Inspector.SetLabel(Id, "samples", Source.Enabled ? $"Path samples: {Source.SampleCount}" : "Path tracing inactive");
        Inspector.SetDropdownValue(Id, "traceBackend", (int)Source.Backend);
        Inspector.SetLabel(Id, "traceStatus", TraceSelectionError ?? Source.BackendStatus + (Source.TraceGpuMilliseconds >= 0 ? $" | {Source.TraceGpuMilliseconds:F2} ms" : ""));
        Inspector.SetSliderValue(Id, "bounces", Source.MaxBounces);
        Inspector.SetSliderValue(Id, "traceScale", Source.ResolutionScale);

        if (Denoiser == null) return;

        if (OptionsRevision != Denoiser.OptionsRevision) { Options = Denoiser.GetOptions(); OptionsRevision = Denoiser.OptionsRevision; Inspector.SetDropdownOptions(Id, "denoiser", OptionNames()); }

        Inspector.SetDropdownValue(Id, "denoiser", SelectedIndex());
        Inspector.SetLabel(Id, "denoiserStatus", SelectionError ?? Denoiser.Status);
        Inspector.SetSliderValue(Id, "denoiseStrength", Denoiser.Strength);
        Inspector.SetSliderValue(Id, "denoiseIterations", Denoiser.Iterations);
        Inspector.SetWidgetEnabled(Id, "denoiseStrength", Denoiser.Enabled && Denoiser.Mode is DenoiserMode.Bilateral or DenoiserMode.Atrous or DenoiserMode.Svgf);
        Inspector.SetWidgetEnabled(Id, "denoiseIterations", Denoiser.Enabled && Denoiser.Mode is DenoiserMode.Atrous or DenoiserMode.Svgf);
        Inspector.SetDropdownValue(NeuralId, "quality", (int)Denoiser.NeuralQuality);
        Inspector.SetDropdownValue(NeuralId, "device", (int)Denoiser.NeuralDevice);
        Inspector.SetSliderValue(NeuralId, "interval", Denoiser.NeuralUpdateInterval);
        Inspector.SetLabel(NeuralId, "status", Denoiser.Status);
    }

    /// <inheritdoc/>
    public override void Dispose() { Inspector.DestroyWindow(Id); Inspector.DestroyWindow(NeuralId); }
}
