using System;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private const int ProfilingChartHeight = 240;
    private static readonly string[] PerformanceRows = ["perfHeader", "fps", "frame", "target", "cpu", "gpu", "memHeader", "ram", "peak"];
    private static readonly string[] GeometryRows = ["geometryHeader", "emissive", "absorption", "buffers", "collect", "flatten", "render", "setdata", "draw", "sdf", "jfa"];

    internal static void AddMetric(string window, string id, string text)
        => Instance?.AddWidgetInternal(window, id, WidgetType.Metric, text);

    internal static void SetFrameHistory(float[] samples, int next, int count, float target)
    {
        if (Instance == null || !Instance.Windows.TryGetValue("perf", out var window)) return;
        if (!window.WidgetIndex.TryGetValue("history", out int index))
        {
            Instance.AddWidgetInternal("perf", "history", WidgetType.FrameHistory, "Frame time");
            index = window.WidgetIndex["history"];
        }
        var widget = window.Widgets[index];
        widget.History = samples;
        widget.HistoryIndex = next;
        widget.HistoryCount = count;
        widget.HistoryTarget = target;
        window.Widgets[index] = widget;
    }

    // The two columns share one gutter. Collapse when labels and measured values would collide.
    private int LayoutProfiling(WindowData window, bool apply)
    {
        int width = (int)window.Size.X;
        int inner = Math.Max(1, width - Padding * 2);
        int gap = Padding;
        bool twoColumns = inner >= 760 && window.WidgetIndex.ContainsKey("geometryHeader");
        int columnWidth = twoColumns ? (inner - gap) / 2 : inner;
        int x = (int)window.Position.X + Padding;
        int start = (int)window.Position.Y + WindowTitleHeight + WidgetSpacing;
        int PlaceRows(string[] ids, int left, int top, int rowWidth)
        {
            foreach (string id in ids)
            {
                if (!window.WidgetIndex.TryGetValue(id, out int index)) continue;
                var widget = window.Widgets[index];
                if (!widget.Visible || string.IsNullOrEmpty(widget.Text)) continue;
                int height = widget.Section ? WidgetHeight : (int)LineHeight + LabelSpacing;
                if (apply)
                {
                    widget.Bounds = new Rectangle(left, top, rowWidth, height);
                    window.Widgets[index] = widget;
                }
                top += height;
                if (widget.Section) top += LabelSpacing;
            }
            return top;
        }
        int leftBottom = PlaceRows(PerformanceRows, x, start, columnWidth);
        int rightBottom = PlaceRows(GeometryRows, twoColumns ? x + columnWidth + gap : x,
            twoColumns ? start : leftBottom + WidgetSpacing, twoColumns ? inner - columnWidth - gap : inner);
        int bottom = Math.Max(leftBottom, rightBottom) + WidgetSpacing;
        if (window.WidgetIndex.TryGetValue("history", out int chartIndex))
        {
            var chart = window.Widgets[chartIndex];
            if (apply)
            {
                chart.Bounds = new Rectangle(x, bottom, inner, ProfilingChartHeight);
                window.Widgets[chartIndex] = chart;
            }
            bottom += ProfilingChartHeight + WidgetSpacing;
        }
        int naturalHeight = bottom - (int)window.Position.Y + Padding;
        if (CurrentWorkspaceMode == WorkspaceMode.Windows)
            naturalHeight = Math.Min(naturalHeight, Math.Max(MinWindowHeight, (int)(Renderer.VirtualHeight / UIScale) - MenuBarHeight - AutoLayoutGap * 2));
        if (apply)
        {
            window.TitleBarBounds = new Rectangle((int)window.Position.X, (int)window.Position.Y, width, WindowTitleHeight);
            window.CloseBounds = CurrentWorkspaceMode == WorkspaceMode.Panels ? default : DockTabCloseBounds(window.TitleBarBounds);
            window.WindowBounds = new Rectangle((int)window.Position.X, (int)window.Position.Y, width,
                window.ResizedHeight > 0 ? (int)window.ResizedHeight : naturalHeight);
        }
        return naturalHeight;
    }

    private void DrawMetric(Widget widget)
    {
        int colon = widget.Text.IndexOf(':');
        if (colon < 0) { DrawLabel(widget); return; }
        string value = widget.Text[(colon + 1)..].Trim();
        var size = MeasureText(value);
        float y = widget.Bounds.Y + (widget.Bounds.Height - size.Y) / 2;
        string label = TruncateText(widget.Text[..colon], Math.Max(0, widget.Bounds.Width - size.X - InlineGap));
        DrawText(label, new Vector2(widget.Bounds.X, y), LabelDim);
        DrawText(value, new Vector2(widget.Bounds.Right - size.X, y), TextColor);
    }

    private void DrawFrameHistory(Widget widget)
    {
        var bounds = widget.Bounds;
        DrawTextBold("Frame time", new Vector2(bounds.X, bounds.Y), TextColor);
        var solid = Renderer.GetSolidTexture(Color.White);
        int count = widget.HistoryCount;
        float peak = widget.HistoryTarget;
        for (int i = 0; i < count; i++) peak = Math.Max(peak, widget.History[i]);
        float maximum = Math.Max(5, MathF.Ceiling(peak / 5) * 5);
        int left = bounds.X + (int)MeasureText($"{maximum:0} ms").X + InlineGap;
        string caption = count == 0 ? "Waiting for frames" : $"Last {count} frames";
        string target = widget.HistoryTarget > 0 ? $"Target {widget.HistoryTarget:0.##} ms" : "Uncapped";
        bool stackedCaption = MeasureText(caption).X + MeasureText(target).X + InlineGap > bounds.Width;
        var plot = new Rectangle(left, bounds.Y + WidgetHeight, Math.Max(1, bounds.Right - left),
            bounds.Height - WidgetHeight * 2 - (stackedCaption ? (int)LineHeight : 0));
        for (int i = 0; i <= 2; i++)
        {
            float value = maximum * (2 - i) / 2;
            int y = plot.Top + i * plot.Height / 2;
            string label = $"{value:0.#}";
            DrawText(label, new Vector2(plot.X - InlineGap - MeasureText(label).X, y - LineHeight / 2), LabelDim);
            Renderer.DrawSprite(solid, new Rectangle(plot.X, y, plot.Width, 1), LabelDim * 0.25f);
        }
        string unit = "ms";
        DrawText(unit, new Vector2(bounds.Right - MeasureText(unit).X, bounds.Y), LabelDim);
        if (count > 0)
        {
            int capacity = widget.History.Length;
            int oldest = (widget.HistoryIndex - count + capacity) % capacity;
            Vector2 PointAt(int i) => new(plot.X + (capacity - count + i) * plot.Width / (float)(capacity - 1),
                plot.Bottom - Math.Clamp(widget.History[(oldest + i) % capacity] / maximum, 0, 1) * plot.Height);
            for (int i = 1; i < count; i++)
            {
                var a = PointAt(i - 1); var b = PointAt(i); var delta = b - a;
                Renderer.DrawSprite(solid, new Rectangle((int)a.X, (int)a.Y, Math.Max(1, (int)delta.Length()), 2), null,
                    SliderFill, MathF.Atan2(delta.Y, delta.X), Vector2.Zero);
            }
            if (count == 1)
            {
                var point = PointAt(0);
                Renderer.DrawSprite(solid, new Rectangle((int)point.X - 2, (int)point.Y - 2, 4, 4), SliderFill);
            }
        }
        if (widget.HistoryTarget > 0)
        {
            int y = plot.Bottom - (int)(widget.HistoryTarget / maximum * plot.Height);
            for (int x = plot.Left; x < plot.Right; x += 12)
                Renderer.DrawSprite(solid, new Rectangle(x, y, Math.Min(6, plot.Right - x), 1), TextColor * 0.5f);
        }
        DrawText(caption, new Vector2(bounds.X, plot.Bottom + WidgetSpacing), LabelDim);
        DrawText(target, new Vector2(stackedCaption ? bounds.X : bounds.Right - MeasureText(target).X,
            plot.Bottom + WidgetSpacing + (stackedCaption ? LineHeight : 0)), LabelDim);
    }
}
