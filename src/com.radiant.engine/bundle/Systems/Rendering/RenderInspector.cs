using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.bundle;

/// <summary>Per-view inspection. Buffer selection never changes any producer's output contract.</summary>
[SystemTag("Rendering", "Tools")]
public class RenderInspector : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.World;
    public string Id = "rendering";
    public string Title = "Rendering";
    public Func<Texture2D> Input;

    private readonly List<(string Name, Func<Texture2D> Texture, IRenderSource Source)> Entries = new();
    private readonly List<int> Visible = new();
    private readonly List<(string Name, IRenderSource Source)> Sources = new();
    private readonly HashSet<(string Name, IRenderSource Source, string Key)> Known = new();
    private RenderTarget2D ScalarPreview;
    private int PreviewIndex = -1;

    public int BufferIndex { get; private set; }
    public int BufferCount => Entries.Count + 1;
    public string BufferName => BufferIndex == 0 ? "Final" : Entries[BufferIndex - 1].Name;

    /// <summary>Registers a render source and discovers its named diagnostic buffers.</summary>
    public void AddSource(string name, IRenderSource source)
    {
        Sources.Add((name, source));
        DiscoverBuffers(name, source);
    }

    /// <summary>Adds newly exposed source buffers while preserving existing inspection indices.</summary>
    private void DiscoverBuffers(string name, IRenderSource source)
    {
        foreach (string key in source.Buffers.Keys) if (Known.Add((name, source, key))) AddEntry(name, source, key);
    }

    /// <summary>Adds a named live buffer getter to the inspector's buffer list.</summary>
    private void AddEntry(string name, IRenderSource source, string key) => Entries.Add(($"{name} / {key}", () => source.Buffers.TryGetValue(key, out var get) ? get() : null, source));

    /// <summary>Selects an available diagnostic buffer by index.</summary>
    public void SelectBuffer(int index)
    {
        if (index < 0 || index > Entries.Count) throw new ArgumentOutOfRangeException(nameof(index));

        BufferIndex = index;
        Inspector.SetDropdownValue(Id, "buffer", Math.Max(0, Visible.IndexOf(index)));
    }

    /// <summary>Reports whether a buffer currently has an enabled source and a live texture.</summary>
    private bool Available(int index) => index == 0 || (Entries[index - 1].Source is not core.System system || system.Enabled) && Entries[index - 1].Texture() is { IsDisposed: false };

    /// <summary>Cycles to the next currently available diagnostic buffer.</summary>
    public void SelectNextBuffer()
    {
        int next = (BufferIndex + 1) % BufferCount;

        while (!Available(next)) next = (next + 1) % BufferCount;

        SelectBuffer(next);
    }

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public Texture2D GetOutput() => BufferIndex == 0 ? Input?.Invoke() : PreviewIndex == BufferIndex ? ScalarPreview : Entries[BufferIndex - 1].Texture();

    /// <inheritdoc/>
    public override void Initialize()
    {
        Visible.Add(0);
        Inspector.CreateWindow(Id, Title);
        Inspector.AddDropdown(Id, "buffer", "Buffer", ["Final"], 0, index => SelectBuffer(Visible[index]));
        Inspector.AddLabel(Id, "resolution", "...");
        Inspector.AddLabel(Id, "preview", "Final output");
    }

    /// <inheritdoc/>
    public override void Render()
    {
        foreach (var source in Sources) DiscoverBuffers(source.Name, source.Source);

        bool changed = false;
        int count = 0;

        for (int i = 0; i < BufferCount; i++)
        {
            if (!Available(i)) continue;

            if (count >= Visible.Count) { Visible.Add(i); changed = true; }
            else if (Visible[count] != i) { Visible[count] = i; changed = true; }

            count++;
        }

        if (count < Visible.Count) { Visible.RemoveRange(count, Visible.Count - count); changed = true; }

        if (changed)
        {
            var names = new string[count];

            for (int i = 0; i < count; i++) names[i] = Visible[i] == 0 ? "Final" : Entries[Visible[i] - 1].Name;

            Inspector.SetDropdownOptions(Id, "buffer", names);
        }

        if (!Available(BufferIndex)) SelectBuffer(0);
        Inspector.SetDropdownValue(Id, "buffer", Visible.IndexOf(BufferIndex));

        var texture = BufferIndex == 0 ? Input?.Invoke() : Entries[BufferIndex - 1].Texture();
        PreviewIndex = -1;

        if (BufferIndex != 0 && texture?.Format == SurfaceFormat.Single)
        {
            if (ScalarPreview?.Width != texture.Width || ScalarPreview.Height != texture.Height)
            {
                Renderer.SetTarget(null);
                ScalarPreview?.Dispose();
                ScalarPreview = Renderer.CreateRenderTarget(texture.Width, texture.Height);
            }

            Renderer.Reset().SetShader("ScalarPreview").Configure(SamplerState.PointClamp).SetTarget(ScalarPreview).SetParameter("Source", texture).Draw().Commit().SetTarget(null);
            PreviewIndex = BufferIndex;
        }
        else if (ScalarPreview != null) { Renderer.SetTarget(null); ScalarPreview.Dispose(); ScalarPreview = null; }

        Inspector.SetLabel(Id, "preview", BufferIndex == 0 ? "Final: image processing applied" : "Raw buffer: exposure / AA bypassed");
        Inspector.SetLabel(Id, "resolution", texture == null ? "Buffer unavailable (stage inactive)" : $"{texture.Width} x {texture.Height} | {texture.Format}");
    }

    /// <inheritdoc/>
    public override void Dispose() { ScalarPreview?.Dispose(); ScalarPreview = null; PreviewIndex = -1; Inspector.DestroyWindow(Id); }
}
