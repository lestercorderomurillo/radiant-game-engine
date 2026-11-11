using System;
using System.Collections.Generic;
using System.Linq;
using com.radiant.engine.input;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private sealed class DockGroup
    {
        public List<WindowData> Windows = new();
        public int ContentHeight;
        public int Height;
        public string Key => Windows[0].Id;
    }

    private readonly List<(Rectangle Bounds, string Above, string Below, int AboveHeight, int BelowHeight)> DockStackDividers = new();
    private readonly Dictionary<string, int> DockStackHeights = new();
    private (string Above, string Below, float StartY, int AboveHeight, int BelowHeight)? DragStack;

    private int DockGroupHeaderHeight(int count, int width)
    {
        int columns = Math.Min(count, Math.Max(1, width / Math.Max(300, ChromeButtonExtent * 2 + 180)));
        return ((count + columns - 1) / columns) * DockHeaderHeight;
    }

    private int MeasureDockContent(WindowData window, int width)
    {
        var size = window.Size;
        float resized = window.ResizedHeight;
        var lists = new List<(int Index, Widget Widget)>();
        try
        {
            window.Size = new Vector2(width, 0);
            window.ResizedHeight = 0;
            for (int i = 0; i < window.Widgets.Count; i++)
            {
                var widget = window.Widgets[i];
                if (widget.Type != WidgetType.ListBox) continue;
                lists.Add((i, widget));
                widget.ListBoxHeight = 160;
                window.Widgets[i] = widget;
            }
            return window.Id == "debugger" ? 240 : Math.Max(120, ComputeWindowHeight(window));
        }
        finally
        {
            window.Size = size;
            window.ResizedHeight = resized;
            foreach (var saved in lists) window.Widgets[saved.Index] = saved.Widget;
        }
    }

    private void LayoutDockStack(List<WindowData> windows, int x, int y, int width, int height, int dock)
    {
        var groups = windows.Select(window => new DockGroup { Windows = new() { window }, ContentHeight = MeasureDockContent(window, width) }).ToList();
        int Required(DockGroup group) => group.ContentHeight + DockGroupHeaderHeight(group.Windows.Count, width);
        // Keep every panel independent while its contents fit. Merge only the adjacent pair
        // that recovers the most height, leaving the other panels visible.
        while (groups.Count > 1 && groups.Sum(Required) > height)
        {
            int best = 0, saving = int.MinValue;
            for (int i = 0; i < groups.Count - 1; i++)
            {
                var a = groups[i]; var b = groups[i + 1];
                int merged = Math.Max(a.ContentHeight, b.ContentHeight) + DockGroupHeaderHeight(a.Windows.Count + b.Windows.Count, width);
                int freed = Required(a) + Required(b) - merged;
                if (freed > saving) { best = i; saving = freed; }
            }
            groups[best].ContentHeight = Math.Max(groups[best].ContentHeight, groups[best + 1].ContentHeight);
            groups[best].Windows.AddRange(groups[best + 1].Windows);
            groups.RemoveAt(best + 1);
        }
        foreach (var group in groups) group.Height = Required(group);
        int extra = Math.Max(0, height - groups.Sum(g => g.Height));
        var flexible = groups.Where(g => g.Windows.Any(w => w.Widgets.Any(widget => widget.Visible && widget.Type == WidgetType.ListBox))).ToList();
        if (flexible.Count == 0)
        {
            // Balance the spare height instead of leaving one oversized panel and two tiny ones.
            while (extra > 0)
            {
                int shortest = groups.Min(g => g.Height);
                var grow = groups.Where(g => g.Height == shortest).ToList();
                int next = groups.Where(g => g.Height > shortest).Select(g => g.Height).DefaultIfEmpty(shortest + extra).Min();
                int share = Math.Min(extra, (next - shortest) * grow.Count);
                for (int i = 0; i < grow.Count; i++) grow[i].Height += share / grow.Count + (i < share % grow.Count ? 1 : 0);
                extra -= share;
            }
        }
        else for (int i = 0; i < flexible.Count; i++) flexible[i].Height += extra / flexible.Count + (i < extra % flexible.Count ? 1 : 0);
        // An explicit divider drag shares space between its two neighbors. On viewport resize,
        // discard stale heights rather than pushing panels outside the dock.
        if (groups.All(g => DockStackHeights.ContainsKey(g.Key)) && groups.Sum(g => DockStackHeights[g.Key]) == height)
            foreach (var group in groups) group.Height = DockStackHeights[group.Key];
        int remaining = height;
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            group.Height = g == groups.Count - 1 ? remaining : Math.Min(group.Height, remaining);
            remaining -= group.Height;
            string selected = group.Windows.Any(w => w.Id == ActiveDock[dock]) ? ActiveDock[dock] : DockGroupSelection.GetValueOrDefault(group.Key);
            var active = group.Windows.FirstOrDefault(w => w.Id == selected) ?? group.Windows[0];
            DockGroupSelection[group.Key] = active.Id;
            DisplayedDockWindows.Add(active.Id);
            int columns = Math.Min(group.Windows.Count, Math.Max(1, width / Math.Max(300, ChromeButtonExtent * 2 + 180)));
            for (int i = 0; i < group.Windows.Count; i++)
            {
                var window = group.Windows[i];
                SaveFloatingLayout(window);
                int row = i / columns, column = i % columns;
                int rowCount = Math.Min(columns, group.Windows.Count - row * columns);
                int left = x + column * width / rowCount;
                int right = x + (column + 1) * width / rowCount;
                DockTabs.Add((window, new Rectangle(left, y + row * DockHeaderHeight, right - left, DockHeaderHeight)));
            }
            int top = y + DockGroupHeaderHeight(group.Windows.Count, width);
            active.Position = new Vector2(x, top);
            active.Size = new Vector2(width, 0);
            active.Resizable = true;
            active.ResizedHeight = Math.Max(1, y + group.Height - top);
            ComputeLayout(active);
            ApplyWindowScroll(active, y + group.Height);
            y += group.Height;
            if (g + 1 < groups.Count)
                DockStackDividers.Add((new Rectangle(x, y - 6, width, 12), group.Key, groups[g + 1].Key, group.Height, groups[g + 1].Height));
        }
    }

    private bool UpdateDockStackInput(Vector2 mouse, bool pressed)
    {
        if (DragStack is { } drag)
        {
            if (!Renderer.IsActive || Controls.Keyboard.IsKeyDown(Key.Escape) || Controls.Mouse.LeftButton != ButtonState.Pressed)
                DragStack = null;
            else
            {
                int total = drag.AboveHeight + drag.BelowHeight;
                int minimum = Math.Min(DockHeaderHeight + 140, total / 2);
                int above = Math.Clamp(drag.AboveHeight + (int)(mouse.Y - drag.StartY), minimum, total - minimum);
                DockStackHeights[drag.Above] = above;
                DockStackHeights[drag.Below] = total - above;
                ComputeDockLayout();
            }
            MouseOverUI = true;
            return true;
        }
        if (!pressed) return false;
        foreach (var divider in DockStackDividers)
            if (divider.Bounds.Contains(mouse))
            {
                foreach (var split in DockStackDividers)
                { DockStackHeights[split.Above] = split.AboveHeight; DockStackHeights[split.Below] = split.BelowHeight; }
                DragStack = (divider.Above, divider.Below, mouse.Y, divider.AboveHeight, divider.BelowHeight);
                MouseOverUI = true;
                return true;
            }
        return false;
    }

    private void DrawDockStackDividers(Vector2 mouse)
    {
        var solid = Renderer.GetSolidTexture(Color.White);
        foreach (var divider in DockStackDividers)
        {
            bool hovered = PanelMenuWindowId == null && OpenDropdownWindowId == null && OpenMenuId == null && divider.Bounds.Contains(mouse);
            var color = hovered || DragStack?.Above == divider.Above ? TextColor : ButtonHover;
            int y = divider.Bounds.Center.Y;
            Renderer.DrawSprite(solid, new Rectangle(divider.Bounds.X, y - 1, divider.Bounds.Width, 2), color);
            for (int i = -1; i <= 1; i++) Renderer.DrawSprite(solid, new Rectangle(divider.Bounds.Center.X + i * 10, y - 2, 3, 4), LabelDim);
        }
    }
}
