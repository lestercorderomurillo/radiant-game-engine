using System;
using System.Collections.Generic;
using System.Linq;
using com.radiant.engine.core;
using com.radiant.engine.input;

namespace com.radiant.engine.bundle;

/// <summary>Floating inspector windows or a docked editor surrounding a game preview.</summary>
public enum WorkspaceMode { Windows, Panels }

public partial class Inspector
{
    /// <summary>The selected workspace style. Windows is the default.</summary>
    public WorkspaceMode CurrentWorkspaceMode { get; private set; }
    private readonly Dictionary<string, (Vector2 Position, Vector2 Size, float Height, bool Resizable)> FloatingLayout = new();
    private readonly Dictionary<string, int> DockScroll = new();
    private readonly List<(WindowData Window, Rectangle Bounds)> DockTabs = new();
    private readonly List<string> DockTabOrder = new();
    private readonly Dictionary<string, int> DockAssignments = new();
    private string DragTabId;
    private Vector2 DragTabStart;
    private bool DragTabMoved;
    private string DropTabId;
    private bool DropTabAfter;
    private int DropDock = -1;
    private Vector2 DragTabMouse;
    private readonly string[] ActiveDock = new string[3];
    private float LeftDockFraction = 0.26f;
    private float RightDockFraction = 0.26f;
    private float BottomDockFraction = 0.30f;
    private int DraggingDock = -1;
    private Rectangle GameViewport;
    private Rectangle GamePane;
    private Rectangle BottomPane;
    private RenderTarget2D WorkspaceSnapshot;
    private RenderTarget2D DockSurface;

    /// <summary>Switches workspace style, preserving the floating window arrangement.</summary>
    public void SetWorkspaceMode(WorkspaceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == CurrentWorkspaceMode) return;
        Dragging = Resizing = DraggingSlider = DraggingListBox = DraggingScrollbar = false;
        DraggingDock = -1;
        CancelTabDrag();
        ClosePanelMenu();
        CloseDropdown();
        ClearTextInputFocus();
        if (mode == WorkspaceMode.Panels)
        {
            FloatingLayout.Clear();
            foreach (var window in Windows.Values) SaveFloatingLayout(window);
            if (Renderer != null && !Windows.ContainsKey("entity_inspector")) OpenEntityInspector();
            if (Renderer != null) CreateDebugger();
        }
        else
        {
            foreach (var window in Windows.Values)
                if (FloatingLayout.TryGetValue(window.Id, out var saved))
                {
                    window.Position = saved.Position;
                    window.Size = saved.Size;
                    window.ResizedHeight = saved.Height;
                    window.Resizable = saved.Resizable;
                }
            LayoutDone = true;
        }
        CurrentWorkspaceMode = mode;
        if (Renderer != null) { ComputeAllLayouts(); UpdatePresentationViewport(); }
    }

    private void SaveFloatingLayout(WindowData window)
    {
        FloatingLayout.TryAdd(window.Id, (window.Position, window.Size, window.ResizedHeight, window.Resizable));
    }

    private int DockFor(WindowData window) => DockAssignments.TryGetValue(window.Id, out int dock)
        ? dock : window.Id == "debugger" ? 2 : window.Id is "entity_inspector" or "system_inspector" ? 0 : 1;
    private void ActivateDock(WindowData window)
    {
        ActiveDock[DockFor(window)] = window.Id;
        CloseDropdown();
        ClearTextInputFocus();
    }
    private bool IsWindowDisplayed(WindowData window) => window.Visible &&
        (CurrentWorkspaceMode == WorkspaceMode.Windows || ActiveDock[DockFor(window)] == window.Id);

    private void ComputeDockLayout()
    {
        int width = Math.Max(1, (int)(Renderer.VirtualWidth / UIScale));
        int height = Math.Max(MenuBarHeight + 1, (int)(Renderer.VirtualHeight / UIScale));
        foreach (var window in Windows.Values.OrderBy(w => w.LayoutOrder).ThenBy(w => w.CreationIndex))
            if (!DockTabOrder.Contains(window.Id)) DockTabOrder.Add(window.Id);
        var groups = DockTabOrder.Where(Windows.ContainsKey).Select(id => Windows[id]).Where(w => w.Visible)
            .GroupBy(DockFor).ToDictionary(g => g.Key, g => g.ToList());
        int left = groups.ContainsKey(0) ? Math.Max(1, (int)(width * LeftDockFraction)) : 0;
        int right = groups.ContainsKey(1) ? Math.Max(1, (int)(width * RightDockFraction)) : 0;
        int availableHeight = height - MenuBarHeight;
        int bottomHeight = groups.ContainsKey(2) ? Math.Max(1, (int)(availableHeight * BottomDockFraction)) : 0;
        GamePane = new Rectangle(left, MenuBarHeight, Math.Max(1, width - left - right), availableHeight - bottomHeight);
        BottomPane = new Rectangle(left, GamePane.Bottom, GamePane.Width, bottomHeight);
        var content = new Rectangle(GamePane.X + 2, GamePane.Y + DockHeaderHeight, Math.Max(1, GamePane.Width - 4), Math.Max(1, GamePane.Height - DockHeaderHeight - 2));
        GameViewport = FitPreview(content, Renderer.ScreenWidth, Renderer.ScreenHeight);
        DockTabs.Clear();
        foreach (var pair in groups)
        {
            int dock = pair.Key;
            var windows = pair.Value;
            if (!windows.Any(w => w.Id == ActiveDock[dock])) ActiveDock[dock] = windows[0].Id;
            int dockWidth = dock == 2 ? BottomPane.Width : dock == 0 ? left : right;
            int x = dock == 2 ? BottomPane.X : dock == 0 ? 0 : width - right;
            int dockTop = dock == 2 ? BottomPane.Y : MenuBarHeight;
            int minTabWidth = Math.Max(230, ChromeButtonExtent * 2 + 180);
            int columns = Math.Min(windows.Count, Math.Max(1, dockWidth / minTabWidth));
            int rows = (windows.Count + columns - 1) / columns;
            int top = dockTop + rows * DockHeaderHeight;
            for (int i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                SaveFloatingLayout(window);
                int row = i / columns;
                int column = i % columns;
                int rowCount = Math.Min(columns, windows.Count - row * columns);
                int tabLeft = x + column * dockWidth / rowCount;
                int tabRight = x + (column + 1) * dockWidth / rowCount;
                DockTabs.Add((window, new Rectangle(tabLeft, dockTop + row * DockHeaderHeight,
                    tabRight - tabLeft, DockHeaderHeight)));
                if (!IsWindowDisplayed(window)) continue;
                window.Position = new Vector2(x, top);
                window.Size = new Vector2(dockWidth, 0);
                window.Resizable = true;
                window.ResizedHeight = Math.Max(1, height - top);
                ComputeLayout(window);
                ApplyWindowScroll(window, height);
            }
        }
    }

    internal static Rectangle FitPreview(Rectangle area, int sourceWidth, int sourceHeight)
    {
        float scale = Math.Min((float)area.Width / sourceWidth, (float)area.Height / sourceHeight);
        int width = Math.Max(1, (int)(sourceWidth * scale));
        int height = Math.Max(1, (int)(sourceHeight * scale));
        return new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    private Rectangle WorkspacePixels(Rectangle rect)
    {
        float x = UIScale * Renderer.ScreenWidth / Renderer.VirtualWidth;
        float y = UIScale * Renderer.ScreenHeight / Renderer.VirtualHeight;
        int left = (int)MathF.Round(rect.X * x), top = (int)MathF.Round(rect.Y * y);
        return new Rectangle(left, top, Math.Max(1, (int)MathF.Round(rect.Right * x) - left), Math.Max(1, (int)MathF.Round(rect.Bottom * y) - top));
    }

    private void UpdatePresentationViewport() => Renderer.ScenePresentationBounds =
        GlobalVisible && CurrentWorkspaceMode == WorkspaceMode.Panels ? WorkspacePixels(GameViewport) : null;

    private bool UpdateDockInput(Vector2 mouse, bool pressed, int scroll)
    {
        if (CurrentWorkspaceMode != WorkspaceMode.Panels) return false;
        if (OpenDropdownWindowId != null) return false;
        float width = Renderer.VirtualWidth / UIScale;
        if (DraggingDock >= 0)
        {
            if (!Renderer.IsActive || Controls.Keyboard.IsKeyDown(Key.Escape)) { DraggingDock = -1; return true; }
            if (Controls.Mouse.LeftButton == ButtonState.Pressed)
            {
                if (DraggingDock == 2)
                {
                    float height = Renderer.VirtualHeight / UIScale;
                    float available = Math.Max(1, height - MenuBarHeight);
                    float minimum = Math.Min(0.65f, Math.Max(0.20f, (DockHeaderHeight + 180) / available));
                    BottomDockFraction = Math.Clamp((height - mouse.Y) / available, minimum, 0.65f);
                }
                else
                {
                    float other = DraggingDock == 0 ? (GamePane.Right < width ? RightDockFraction : 0) : (GamePane.X > 0 ? LeftDockFraction : 0);
                    float minimum = Math.Min(0.26f, MinWindowWidth / width);
                    float maximum = Math.Max(minimum, Math.Min(0.50f, 1 - other - Math.Max(0.24f, MinWindowWidth / width)));
                    if (DraggingDock == 0) LeftDockFraction = Math.Clamp(mouse.X / width, minimum, maximum);
                    else RightDockFraction = Math.Clamp((width - mouse.X) / width, minimum, maximum);
                }
                ComputeDockLayout();
                UpdatePresentationViewport();
            }
            else DraggingDock = -1;
            MouseOverUI = true;
            return true;
        }
        if (pressed && mouse.Y > MenuBarHeight && OpenDropdownWindowId == null &&
            !DockTabs.Any(tab => tab.Bounds.Contains(mouse) && (DockTabCloseBounds(tab.Bounds).Contains(mouse) || DockTabMenuBounds(tab.Bounds).Contains(mouse))))
        {
            if (GamePane.X > 0 && Math.Abs(mouse.X - GamePane.X) <= 10) DraggingDock = 0;
            else if (GamePane.Right < width && Math.Abs(mouse.X - GamePane.Right) <= 10) DraggingDock = 1;
            else if (BottomPane.Height > 0 && mouse.X >= BottomPane.X && mouse.X < BottomPane.Right && Math.Abs(mouse.Y - BottomPane.Y) <= 10) DraggingDock = 2;
            if (DraggingDock >= 0) { MouseOverUI = true; return true; }
        }
        foreach (var tab in DockTabs)
            if (tab.Bounds.Contains(mouse))
            {
                if (pressed)
                {
                    if (DockTabCloseBounds(tab.Bounds).Contains(mouse))
                    {
                        SetWindowVisible(tab.Window.Id, false);
                        if (OpenDropdownWindowId == tab.Window.Id) CloseDropdown();
                        SyncViewToggleValues();
                    }
                    else if (DockTabMenuBounds(tab.Bounds).Contains(mouse)) OpenPanelMenu(tab.Window, tab.Bounds);
                    else
                    {
                        ActivateDock(tab.Window);
                        DragTabId = tab.Window.Id;
                        DragTabStart = DragTabMouse = mouse;
                        DragTabMoved = false;
                    }
                    ComputeDockLayout();
                    UpdatePresentationViewport();
                }
                MouseOverUI = true;
                return true;
            }
        return UpdateWindowScroll(mouse, scroll);
    }

    private void CancelTabDrag()
    {
        DragTabId = DropTabId = null;
        DragTabMoved = false;
        DropDock = -1;
    }

    // Capture the pointer through release, including when it leaves the tab strip.
    private bool UpdateDockTabDrag(Vector2 mouse, bool held)
    {
        if (DragTabId == null) return false;
        DragTabMouse = mouse;
        MouseOverUI = true;
        if (!Renderer.IsActive || Controls.Keyboard.IsKeyDown(Key.Escape) ||
            !Windows.TryGetValue(DragTabId, out var source) || !source.Visible)
        {
            CancelTabDrag();
            return true;
        }
        if (Vector2.Distance(mouse, DragTabStart) >= 8) DragTabMoved = true;
        DropTabId = null;
        DropDock = -1;
        if (DragTabMoved)
            foreach (var tab in DockTabs)
                if (tab.Window.Id != DragTabId && tab.Bounds.Contains(mouse))
                {
                    DropTabId = tab.Window.Id;
                    DropTabAfter = mouse.X >= tab.Bounds.Center.X;
                    DropDock = DockFor(tab.Window);
                    break;
                }
        if (DragTabMoved && DropDock < 0) DropDock = DockDropTarget(mouse);
        if (!held)
        {
            if (DropTabId != null && Windows.TryGetValue(DropTabId, out var target))
            {
                DockTabOrder.Remove(DragTabId);
                int index = DockTabOrder.IndexOf(DropTabId) + (DropTabAfter ? 1 : 0);
                DockTabOrder.Insert(index, DragTabId);
                MovePanel(source, DockFor(target));
            }
            else if (DropDock >= 0) MovePanel(source, DropDock);
            ComputeDockLayout();
            UpdatePresentationViewport();
            CancelTabDrag();
        }
        return true;
    }

    private static Color OpaqueWorkspaceColor(Color color) => new(color.R, color.G, color.B, (byte)255);

    private void DrawPanelsWorkspace()
    {
        if (Renderer.SceneRT == null) return;
        if (WorkspaceSnapshot == null || WorkspaceSnapshot.Width != Renderer.ScreenWidth || WorkspaceSnapshot.Height != Renderer.ScreenHeight)
        {
            WorkspaceSnapshot?.Dispose();
            DockSurface?.Dispose();
            WorkspaceSnapshot = Renderer.CreateRenderTarget(Renderer.ScreenWidth, Renderer.ScreenHeight);
            DockSurface = Renderer.CreateRenderTarget(Renderer.ScreenWidth, Renderer.ScreenHeight);
        }
        BlurResult = null;
        Renderer.SetTarget(WorkspaceSnapshot);
        Renderer.Blit(Renderer.SceneRT, BlendState.Opaque);
        Renderer.SetTarget(null).Clear(OpaqueWorkspaceColor(WindowBg));
        var scale = Matrix.CreateScale(Renderer.ScreenWidth / Renderer.VirtualWidth * UIScale, Renderer.ScreenHeight / Renderer.VirtualHeight * UIScale, 1f);
        UpdatePresentationViewport();
        var mouse = ScreenToVirtual(Controls.Mouse.Position);
        var solid = Renderer.GetSolidTexture(Color.White);
        Renderer.BeginDraw(transform: scale);
        Renderer.DrawSprite(solid, GamePane, Color.Black);
        Renderer.DrawSprite(solid, new Rectangle(GamePane.X, GamePane.Y, GamePane.Width, DockHeaderHeight), OpaqueWorkspaceColor(TitleBarColor));
        DrawTextBold("Game", new Vector2(GamePane.X + Padding, GamePane.Y + (DockHeaderHeight - MeasureText("Game").Y) / 2), TextColor);
        Renderer.DrawSprite(WorkspaceSnapshot, GameViewport, Color.White);
        Renderer.EndDraw();
        foreach (var window in RenderOrder)
        {
            if (!IsWindowDisplayed(window)) continue;
            DrawClippedWindow(window, mouse, OpenMenuId != null || OpenDropdownWindowId != null || PanelMenuWindowId != null || DragTabId != null, scale);
        }
        Renderer.BeginDraw(transform: scale);
        foreach (var tab in DockTabs)
        {
            Renderer.DrawSprite(solid, tab.Bounds, OpaqueWorkspaceColor(IsWindowDisplayed(tab.Window) ? ButtonHover : TitleBarColor));
            var close = DockTabCloseBounds(tab.Bounds);
            var menu = DockTabMenuBounds(tab.Bounds);
            var (label, size) = FitMenuItemText(tab.Window.Title, menu.X - tab.Bounds.X - 18);
            var labelSize = Renderer.MeasureString("Inter", size, label);
            Renderer.DrawString("Inter", size, label, new Vector2(tab.Bounds.X + 10, tab.Bounds.Y + (tab.Bounds.Height - labelSize.Y) / 2), IsWindowDisplayed(tab.Window) ? TextColor : LabelDim);
            bool canHover = OpenMenuId == null && OpenDropdownWindowId == null && PanelMenuWindowId == null && DragTabId == null;
            DrawChromeButton(close, canHover && close.Contains(mouse), true);
            DrawChromeButton(menu, canHover && menu.Contains(mouse), false);
        }
        if (DragTabMoved && DropTabId != null)
            foreach (var tab in DockTabs)
                if (tab.Window.Id == DropTabId)
                    Renderer.DrawSprite(solid, new Rectangle(
                        (DropTabAfter ? tab.Bounds.Right : tab.Bounds.Left) - 2,
                        tab.Bounds.Top + 3, 4, tab.Bounds.Height - 6), TextColor);
        DrawDockDividers(mouse);
        DrawDockDragPreview();
        DrawDropdownPopup(mouse);
        DrawMenuBar(mouse);
        DrawMenuDropdown(mouse);
        DrawWorkspaceSubmenu(mouse);
        DrawPanelMenu(mouse);
        Renderer.EndDraw();
    }
}
