using System;
using System.Linq;
using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private long DebuggerRevision = -1;
    private bool FollowLog = true;
    private LogEntry[] DebuggerEntries = Array.Empty<LogEntry>();
    private bool DebuggerFilterDirty = true;
    private string DebuggerSearch = "";
    private int DebuggerLevel;

    private void CreateDebugger()
    {
        if (Windows.ContainsKey("debugger")) return;
        CreateWindow("debugger", "Debugger", 200);
        AddButton("debugger", "clear", "Clear", EngineLog.Clear);
        AddToggle("debugger", "follow", "Follow latest", true, value => FollowLog = value);
        AddTextInput("debugger", "search", "Search logs...", value => { DebuggerSearch = value; DebuggerFilterDirty = true; });
        AddDropdown("debugger", "level", "", ["All levels", "Info", "Warnings", "Errors"], 0,
            value => { DebuggerLevel = value; DebuggerFilterDirty = true; });
        AddListBox("debugger", "log", 240);
        Windows["debugger"].Resizable = true;
        DebuggerRevision = -1;
    }

    private void UpdateDebugger()
    {
        if (!Windows.TryGetValue("debugger", out var window) || !IsWindowDisplayed(window)) return;
        bool newMessages = EngineLog.TryRead(ref DebuggerRevision, out var entries);
        if (newMessages)
        { DebuggerEntries = entries; DebuggerFilterDirty = true; }
        if (DebuggerFilterDirty)
        {
            SetListBoxItems("debugger", "log", DebuggerEntries
                .Where(entry => DebuggerLevel == 0 || (int)entry.Level == DebuggerLevel - 1)
                .Select(entry => $"{entry.Time:HH:mm:ss} [{entry.Level}] {entry.Source}: {entry.Message}")
                .Where(line => line.Contains(DebuggerSearch, StringComparison.OrdinalIgnoreCase)).ToArray());
            DebuggerFilterDirty = false;
            int logIndex = window.WidgetIndex["log"];
            var logWidget = window.Widgets[logIndex];
            logWidget.ListBoxEmptyText = DebuggerLevel != 0 || DebuggerSearch.Length > 0 ? "No matching log messages" : "No log messages";
            window.Widgets[logIndex] = logWidget;
        }
        if (FollowLog && newMessages)
        {
            int index = window.WidgetIndex["log"];
            var widget = window.Widgets[index];
            int visibleRows = System.Math.Max(1, (widget.Bounds.Height - 8) / (int)(LineHeight + 4));
            widget.ListBoxScroll = System.Math.Max(0, widget.ListBoxItems.Length - visibleRows);
            window.Widgets[index] = widget;
        }
    }
    // A compact two-row toolbar gives the log the remaining space, even in the bottom dock.
    private void ComputeDebuggerLayout(WindowData window)
    {
        int x = (int)window.Position.X, y = (int)window.Position.Y, width = (int)window.Size.X;
        int height = window.ResizedHeight > 0 ? (int)window.ResizedHeight : 440;
        window.TitleBarBounds = new Rectangle(x, y, width, WindowTitleHeight);
        window.CloseBounds = CurrentWorkspaceMode == WorkspaceMode.Panels ? default : DockTabCloseBounds(window.TitleBarBounds);
        window.WindowBounds = new Rectangle(x, y, width, height);
        const int inset = Padding, gap = InlineGap, rowHeight = WidgetHeight;
        int innerWidth = Math.Max(1, width - inset * 2);
        int top = y + WindowTitleHeight + gap;
        void Place(string id, Rectangle bounds)
        {
            int index = window.WidgetIndex[id];
            var widget = window.Widgets[index];
            widget.Bounds = bounds;
            window.Widgets[index] = widget;
        }
        int clearWidth = Math.Min((int)MathF.Ceiling(MeasureText("Clear").X) + Padding * 2, innerWidth);
        Place("clear", new Rectangle(x + inset, top, clearWidth, rowHeight));
        Place("follow", new Rectangle(x + inset + clearWidth + gap, top, Math.Max(1, innerWidth - clearWidth - gap), rowHeight));
        top += rowHeight + gap;
        int levelWidth = Math.Min(200, innerWidth / 2);
        int searchWidth = Math.Max(1, innerWidth - levelWidth - gap);
        Place("search", new Rectangle(x + inset, top, searchWidth, rowHeight));
        Place("level", new Rectangle(x + inset + searchWidth + gap, top, levelWidth, rowHeight));
        top += rowHeight + gap;
        Place("log", new Rectangle(x + inset, top, innerWidth, Math.Max(40, y + height - top - gap)));
    }

}
