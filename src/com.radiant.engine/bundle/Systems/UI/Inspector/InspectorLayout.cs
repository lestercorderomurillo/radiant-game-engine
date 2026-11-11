using System;
using System.Collections.Generic;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private int WindowTitleHeight => CurrentWorkspaceMode == WorkspaceMode.Panels ? 0 : TitleBarHeight;

    /// <summary> Places a single new window in the first available spot without moving existing windows. </summary>
    private void PlaceNewWindow(WindowData NewWindow)
    {
        if (!NewWindow.AutoPosition)
        {
            int windowHeight = ComputeWindowHeight(NewWindow);
            NewWindow.Position = new Vector2(
                (Renderer.VirtualWidth / UIScale - NewWindow.Size.X) / 2,
                (Renderer.VirtualHeight / UIScale - windowHeight) / 2);
            return;
        }

        int newWidth = (int)NewWindow.Size.X;
        int newHeight = ComputeWindowHeight(NewWindow);
        float screenW = Renderer.VirtualWidth / UIScale;
        float screenH = Renderer.VirtualHeight / UIScale - AutoLayoutGap;
        float startY = MenuBarHeight + AutoLayoutGap;

        var existing = new List<Rectangle>();

        foreach (var window in Windows.Values)
        {
            if (window == NewWindow || !window.Visible) continue;

            existing.Add(new Rectangle((int)window.Position.X, (int)window.Position.Y, (int)window.Size.X, ComputeWindowHeight(window)));
        }

        var tryX = new List<float> { AutoLayoutGap };

        foreach (var rect in existing)
            tryX.Add(rect.Right + AutoLayoutGap);

        tryX.Sort();

        foreach (float candidateX in tryX)
        {
            if (candidateX + newWidth > screenW) continue;

            var tryY = new List<float> { startY };

            foreach (var rect in existing)
                tryY.Add(rect.Bottom + AutoLayoutGap);

            tryY.Sort();

            foreach (float candidateY in tryY)
            {
                if (candidateY + newHeight > screenH) continue;

                var candidate = new Rectangle((int)candidateX, (int)candidateY, newWidth, newHeight);
                bool overlaps = false;

                foreach (var rect in existing)
                {
                    if (candidate.Intersects(rect)) { overlaps = true; break; }
                }

                if (!overlaps)
                {
                    NewWindow.Position = new Vector2(candidateX, candidateY);
                    return;
                }
            }
        }

        float fallbackX = global::System.Math.Clamp(AutoLayoutGap, 0, Math.Max(0, screenW - newWidth));
        float fallbackY = global::System.Math.Clamp(startY, startY, Math.Max(startY, screenH - newHeight));
        int leastOverlap = int.MaxValue;

        foreach (float candidateX in tryX)
        {
            float clampedX = global::System.Math.Clamp(candidateX, 0, Math.Max(0, screenW - newWidth));
            var candidate = new Rectangle((int)clampedX, (int)startY, newWidth, newHeight);
            int count = 0;

            foreach (var rect in existing)
            {
                if (candidate.Intersects(rect)) count++;
            }

            if (count < leastOverlap)
            {
                leastOverlap = count;
                fallbackX = clampedX;
                fallbackY = startY;
            }
        }

        NewWindow.Position = new Vector2(fallbackX, fallbackY);
    }

    /// <summary> Arranges all windows in columns, wrapping to a new column when vertical space runs out. </summary>
    private void AutoPositionAll()
    {
        var ordered = new List<WindowData>(Windows.Values);
        ordered.Sort((a, b) =>
        {
            int order = a.LayoutOrder.CompareTo(b.LayoutOrder);
            return order != 0 ? order : a.CreationIndex.CompareTo(b.CreationIndex);
        });

        float columnX = AutoLayoutGap;
        float currentY = MenuBarHeight + AutoLayoutGap;
        float maxY = Renderer.VirtualHeight / UIScale - 64;

        foreach (var window in ordered)
        {
            if (!window.Visible || !window.AutoPosition) continue;

            int windowHeight = ComputeWindowHeight(window);

            if (currentY + windowHeight > maxY && currentY > MenuBarHeight + AutoLayoutGap)
            {
                columnX += DefaultWindowWidth + AutoLayoutGap;
                currentY = MenuBarHeight + AutoLayoutGap;
            }

            window.Position = new Vector2(columnX, currentY);
            currentY += windowHeight + AutoLayoutGap;
        }

        foreach (var window in ordered)
        {
            if (window.Visible || !window.AutoPosition) continue;

            int windowHeight = ComputeWindowHeight(window);

            if (currentY + windowHeight > maxY && currentY > MenuBarHeight + AutoLayoutGap)
            {
                columnX += DefaultWindowWidth + AutoLayoutGap;
                currentY = MenuBarHeight + AutoLayoutGap;
            }

            window.Position = new Vector2(columnX, currentY);
            currentY += windowHeight + AutoLayoutGap;
        }

        foreach (var window in ordered)
        {
            if (window.AutoPosition) continue;

            int windowHeight = ComputeWindowHeight(window);
            window.Position = new Vector2(
                (Renderer.VirtualWidth / UIScale - window.Size.X) / 2,
                (Renderer.VirtualHeight / UIScale - windowHeight) / 2);
        }
    }

    /// <summary>Computes a widget's layout height, including dynamic list content.</summary>
    private int GetWidgetHeight(Widget Widget, int ContentWidth) => Widget.Type switch
    {
        WidgetType.Slider => WidgetHeight + 18,
        WidgetType.Label => Widget.Section ? WidgetHeight : MeasureWrappedHeight(Widget.Text, ContentWidth),
        WidgetType.Metric => (int)LineHeight + LabelSpacing,
        WidgetType.FrameHistory => ProfilingChartHeight,
        WidgetType.ListBox => Widget.ListBoxHeight,
        _ => WidgetHeight
    };

    /// <summary>Reports whether the following widget shares the current layout row.</summary>
    private bool IsNextWidgetInline(WindowData Window, int CurrentIndex)
    {
        for (int j = CurrentIndex + 1; j < Window.Widgets.Count; j++)
        {
            if (!Window.Widgets[j].Visible) continue;

            return Window.Widgets[j].InlineRatio > 0;
        }

        return false;
    }

    // Split the available width after subtracting all gaps; cumulative rounding closes the row exactly.
    private int GetInlineWidgetWidth(WindowData window, int index, int contentWidth)
    {
        int start = index;
        for (int i = index - 1; i >= 0; i--)
        {
            if (!window.Widgets[i].Visible) continue;
            if (window.Widgets[i].InlineRatio <= 0) break;
            start = i;
        }
        float total = 0, before = 0;
        int count = 0;
        for (int i = start; i < window.Widgets.Count; i++)
        {
            var widget = window.Widgets[i];
            if (!widget.Visible) continue;
            if (widget.InlineRatio <= 0) break;
            total += widget.InlineRatio;
            if (i < index) before += widget.InlineRatio;
            count++;
        }
        int available = Math.Max(1, contentWidth - InlineGap * (count - 1));
        return Math.Max(1, (int)MathF.Round(available * (before + window.Widgets[index].InlineRatio) / total)
            - (int)MathF.Round(available * before / total));
    }

    /// <summary> Calculates the total pixel height of a window based on its visible widgets. </summary>
    private int ComputeWindowHeight(WindowData Window)
    {
        if (Window.Resizable && Window.ResizedHeight > 0) return (int)Window.ResizedHeight;
        if (Window.Id == "debugger" && Window.WidgetIndex.ContainsKey("log")) return 440;

        if (Window.Id == "perf") return LayoutProfiling(Window, false);

        int contentWidth = (int)Window.Size.X - Padding * 2;
        int height = WindowTitleHeight + WidgetSpacing;
        int inlineMaxH = 0;
        bool inRow = false;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];

            if (!widget.Visible) continue;

            int widgetH = GetWidgetHeight(widget, widget.InlineRatio > 0 ? GetInlineWidgetWidth(Window, i, contentWidth) : contentWidth);

            if (widget.InlineRatio > 0)
            {
                inlineMaxH = Math.Max(inlineMaxH, widgetH);
                inRow = true;

                if (!IsNextWidgetInline(Window, i))
                {
                    height += inlineMaxH + WidgetSpacing;
                    inlineMaxH = 0;
                    inRow = false;
                }

                continue;
            }

            if (inRow)
            {
                height += inlineMaxH + WidgetSpacing;
                inlineMaxH = 0;
                inRow = false;
            }

            bool tightLabel = widget.Type == WidgetType.Label && !widget.Section;
            bool nextIsTightLabel = false;

            for (int j = i + 1; j < Window.Widgets.Count; j++)
            {
                if (!Window.Widgets[j].Visible) continue;

                nextIsTightLabel = Window.Widgets[j].Type == WidgetType.Label && !Window.Widgets[j].Section;
                break;
            }

            height += widgetH + (tightLabel && nextIsTightLabel ? LabelSpacing : WidgetSpacing);
        }

        if (inRow) height += inlineMaxH + WidgetSpacing;

        return height + Padding;
    }

    /// <summary>Calculates list height from its row count and layout limits.</summary>
    private int ComputeDynamicListBoxHeight(WindowData Window)
    {
        int contentWidth = (int)Window.Size.X - Padding * 2;
        int fixedHeight = WindowTitleHeight + WidgetSpacing + Padding;
        int listBoxCount = 0;
        int inlineMaxH = 0;
        bool inRow = false;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];

            if (!widget.Visible) continue;

            if (widget.Type == WidgetType.ListBox)
            {
                listBoxCount++;
                fixedHeight += WidgetSpacing;
                continue;
            }

            int widgetH = GetWidgetHeight(widget, widget.InlineRatio > 0 ? GetInlineWidgetWidth(Window, i, contentWidth) : contentWidth);

            if (widget.InlineRatio > 0)
            {
                inlineMaxH = Math.Max(inlineMaxH, widgetH);
                inRow = true;

                if (!IsNextWidgetInline(Window, i))
                {
                    fixedHeight += inlineMaxH + WidgetSpacing;
                    inlineMaxH = 0;
                    inRow = false;
                }

                continue;
            }

            if (inRow)
            {
                fixedHeight += inlineMaxH + WidgetSpacing;
                inlineMaxH = 0;
                inRow = false;
            }

            bool tightLabel = widget.Type == WidgetType.Label && !widget.Section;
            bool nextIsTightLabel = false;

            for (int j = i + 1; j < Window.Widgets.Count; j++)
            {
                if (!Window.Widgets[j].Visible) continue;

                nextIsTightLabel = Window.Widgets[j].Type == WidgetType.Label && !Window.Widgets[j].Section;
                break;
            }

            fixedHeight += widgetH + (tightLabel && nextIsTightLabel ? LabelSpacing : WidgetSpacing);
        }

        if (inRow) fixedHeight += inlineMaxH + WidgetSpacing;

        int remaining = (int)Window.ResizedHeight - fixedHeight;
        if (listBoxCount > 0) remaining /= listBoxCount;

        return Math.Max(80, remaining);
    }

    /// <summary> Recomputes layout bounds for all visible windows in render order. </summary>
    private void ComputeAllLayouts()
    {
        if (CurrentWorkspaceMode == WorkspaceMode.Panels) { ComputeDockLayout(); return; }
        foreach (var window in RenderOrder)
        {
            if (!window.Visible) continue;

            ComputeLayout(window);
            ApplyWindowScroll(window, window.WindowBounds.Bottom);
        }
    }

    /// <summary> Computes title bar, close button, widget, and overall bounds for a single window. </summary>
    private void ComputeLayout(WindowData Window)
    {
        if (Window.Id == "debugger" && Window.WidgetIndex.ContainsKey("log")) { ComputeDebuggerLayout(Window); return; }
        if (Window.Id == "perf") { LayoutProfiling(Window, true); return; }
        int posX = (int)Window.Position.X;
        int posY = (int)Window.Position.Y;
        int windowWidth = (int)Window.Size.X;

        int dynamicListBoxHeight = (Window.Resizable && Window.ResizedHeight > 0)
            ? ComputeDynamicListBoxHeight(Window) : -1;

        Window.TitleBarBounds = new Rectangle(posX, posY, windowWidth, WindowTitleHeight);
        Window.CloseBounds = CurrentWorkspaceMode == WorkspaceMode.Panels ? default : DockTabCloseBounds(Window.TitleBarBounds);

        int contentWidth = windowWidth - Padding * 2;
        int widgetY = posY + WindowTitleHeight + WidgetSpacing;
        int inlineX = posX + Padding;
        int inlineMaxH = 0;
        bool inRow = false;

        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];

            if (!widget.Visible) continue;

            int widgetH = (widget.Type == WidgetType.ListBox && dynamicListBoxHeight > 0)
                ? dynamicListBoxHeight : GetWidgetHeight(widget, widget.InlineRatio > 0 ? GetInlineWidgetWidth(Window, i, contentWidth) : contentWidth);

            if (widget.InlineRatio > 0)
            {
                if (!inRow) { inlineX = posX + Padding; inlineMaxH = 0; inRow = true; }

                int widgetWidth = GetInlineWidgetWidth(Window, i, contentWidth);
                widget.Bounds = new Rectangle(inlineX, widgetY, widgetWidth, widgetH);
                Window.Widgets[i] = widget;
                inlineX += widgetWidth + InlineGap;
                inlineMaxH = Math.Max(inlineMaxH, widgetH);

                if (!IsNextWidgetInline(Window, i))
                {
                    widgetY += inlineMaxH + WidgetSpacing;
                    inRow = false;
                }

                continue;
            }

            if (inRow)
            {
                widgetY += inlineMaxH + WidgetSpacing;
                inRow = false;
            }

            int standaloneWidth = widget.Compact ? Math.Min(contentWidth, (int)MathF.Ceiling(MeasureText(widget.Text).X) + Padding * 2) : contentWidth;
            widget.Bounds = new Rectangle(posX + Padding, widgetY, standaloneWidth, widgetH);
            Window.Widgets[i] = widget;

            bool tightLabel = widget.Type == WidgetType.Label && !widget.Section;
            bool nextIsTightLabel = false;

            for (int j = i + 1; j < Window.Widgets.Count; j++)
            {
                if (!Window.Widgets[j].Visible) continue;

                nextIsTightLabel = Window.Widgets[j].Type == WidgetType.Label && !Window.Widgets[j].Section;
                break;
            }

            widgetY += widgetH + (tightLabel && nextIsTightLabel ? LabelSpacing : WidgetSpacing);
        }

        if (inRow) widgetY += inlineMaxH + WidgetSpacing;

        int totalHeight = (Window.Resizable && Window.ResizedHeight > 0)
            ? (int)Window.ResizedHeight : widgetY - posY + Padding;
        Window.WindowBounds = new Rectangle(posX, posY, windowWidth, totalHeight);
    }

    /// <summary> Measures the pixel height of text that word-wraps within the given width. </summary>
    private int MeasureWrappedHeight(string Text, int AvailableWidth)
    {
        int maxWidth = AvailableWidth;

        if (MeasureText(Text).X <= maxWidth) return (int)MeasureText(Text).Y;

        string[] words = Text.Split(' ');
        float spaceWidth = MeasureText(" ").X;
        int lines = 1;
        float lineWidth = 0;

        for (int i = 0; i < words.Length; i++)
        {
            float wordWidth = MeasureText(words[i]).X;
            float addWidth = lineWidth == 0 ? wordWidth : spaceWidth + wordWidth;

            if (lineWidth + addWidth > maxWidth && lineWidth > 0)
            {
                lines++;
                lineWidth = wordWidth;
            }
            else
            {
                lineWidth += addWidth;
            }
        }

        return (int)(lines * LineHeight + 8);
    }

    /// <summary> Converts screen-space pixel coordinates to virtual-space coordinates adjusted for UI scale. </summary>
    private Vector2 ScreenToVirtual(Vector2 ScreenPos)
    {
        return new Vector2(
            ScreenPos.X * (Renderer.VirtualWidth / Renderer.ScreenWidth) / UIScale,
            ScreenPos.Y * (Renderer.VirtualHeight / Renderer.ScreenHeight) / UIScale);
    }

    /// <summary>Keeps body text at 18 screen pixels instead of changing size in half-step jumps.</summary>
    private float ComputeAutoScale() => Math.Clamp(18f / FontSize * Renderer.VirtualHeight / Renderer.ScreenHeight, 0.5f, 3f);
}
