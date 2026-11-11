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
    private readonly string[] ActiveDock = new string[2];
    private float LeftDockFraction = 0.26f;
    private float RightDockFraction = 0.26f;
    private int DraggingDock = -1;
    private Rectangle GameViewport;
    private Rectangle GamePane;
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
        CloseDropdown();
        ClearTextInputFocus();
        if (mode == WorkspaceMode.Panels)
        {
            FloatingLayout.Clear();
            foreach (var window in Windows.Values) SaveFloatingLayout(window);
            if (Renderer != null && !Windows.ContainsKey("entity_inspector")) OpenEntityInspector();
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
        ? dock : window.Id is "entity_inspector" or "system_inspector" ? 0 : 1;
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
        GamePane = new Rectangle(left, MenuBarHeight, Math.Max(1, width - left - right), height - MenuBarHeight);
        var content = new Rectangle(GamePane.X + 2, GamePane.Y + TitleBarHeight, Math.Max(1, GamePane.Width - 4), Math.Max(1, GamePane.Height - TitleBarHeight - 2));
        GameViewport = FitPreview(content, Renderer.ScreenWidth, Renderer.ScreenHeight);
        DockTabs.Clear();
        foreach (var pair in groups)
        {
            int dock = pair.Key;
            var windows = pair.Value;
            if (!windows.Any(w => w.Id == ActiveDock[dock])) ActiveDock[dock] = windows[0].Id;
            int dockWidth = dock == 0 ? left : right;
            int x = dock == 0 ? 0 : width - right;
            int columns = Math.Max(1, dockWidth / 230);
            int tabWidth = dockWidth / columns;
            int rows = (windows.Count + columns - 1) / columns;
            int top = MenuBarHeight + rows * MenuItemHeight;
            for (int i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                SaveFloatingLayout(window);
                DockTabs.Add((window, new Rectangle(x + i % columns * tabWidth, MenuBarHeight + i / columns * MenuItemHeight, tabWidth, MenuItemHeight)));
                if (!IsWindowDisplayed(window)) continue;
                window.Position = new Vector2(x, top);
                window.Size = new Vector2(dockWidth, 0);
                window.Resizable = true;
                window.ResizedHeight = Math.Max(1, height - top);
                ComputeLayout(window);
                int bottom = window.Widgets.Where(w => w.Visible).Select(w => w.Bounds.Bottom).DefaultIfEmpty(top).Max() + Padding;
                int maxScroll = Math.Max(0, bottom - height);
                int scroll = Math.Clamp(DockScroll.GetValueOrDefault(window.Id), 0, maxScroll);
                DockScroll[window.Id] = scroll;
                for (int iWidget = 0; iWidget < window.Widgets.Count; iWidget++)
                {
                    var widget = window.Widgets[iWidget];
                    widget.Bounds = new Rectangle(widget.Bounds.X, widget.Bounds.Y - scroll, widget.Bounds.Width, widget.Bounds.Height);
                    window.Widgets[iWidget] = widget;
                }
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

    private static Rectangle DockTabCloseBounds(Rectangle tab) => new(
        tab.Right - CloseButtonSize - 7, tab.Y + (tab.Height - CloseButtonSize) / 2,
        CloseButtonSize, CloseButtonSize);

    private bool UpdateDockInput(Vector2 mouse, bool pressed, int scroll)
    {
        if (CurrentWorkspaceMode != WorkspaceMode.Panels) return false;
        float width = Renderer.VirtualWidth / UIScale;
        if (DraggingDock >= 0)
        {
            if (Controls.Mouse.LeftButton == ButtonState.Pressed)
            {
                if (DraggingDock == 0) LeftDockFraction = Math.Clamp(mouse.X / width, 0.18f, 0.38f);
                else RightDockFraction = Math.Clamp((width - mouse.X) / width, 0.18f, 0.38f);
                ComputeDockLayout();
                UpdatePresentationViewport();
            }
            else DraggingDock = -1;
            MouseOverUI = true;
            return true;
        }
        if (pressed && mouse.Y > MenuBarHeight && OpenDropdownWindowId == null)
        {
            if (GamePane.X > 0 && Math.Abs(mouse.X - GamePane.X) <= 6) DraggingDock = 0;
            else if (GamePane.Right < width && Math.Abs(mouse.X - GamePane.Right) <= 6) DraggingDock = 1;
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
                    else
                    {
                        ActivateDock(tab.Window);
                        DragTabId = tab.Window.Id;
                        DragTabStart = mouse;
                        DragTabMoved = false;
                    }
                    ComputeDockLayout();
                    UpdatePresentationViewport();
                }
                MouseOverUI = true;
                return true;
            }
        if (scroll != 0 && OpenDropdownWindowId == null)
            foreach (var window in Windows.Values)
            {
                if (!IsWindowDisplayed(window) || !window.WindowBounds.Contains(mouse)) continue;
                // Lists keep their own scrolling; the rest of a panel scrolls as one document.
                if (window.Widgets.Any(w => w.Visible && w.Type == WidgetType.ListBox && w.Bounds.Contains(mouse))) return false;
                DockScroll[window.Id] = Math.Max(0, DockScroll.GetValueOrDefault(window.Id) - Math.Sign(scroll) * 90);
                ComputeDockLayout();
                return true;
            }
        return false;
    }

    private void CancelTabDrag()
    {
        DragTabId = DropTabId = null;
        DragTabMoved = false;
    }

    // Capture the pointer through release, including when it leaves the tab strip.
    private bool UpdateDockTabDrag(Vector2 mouse, bool held)
    {
        if (DragTabId == null) return false;
        MouseOverUI = true;
        if (!Renderer.IsActive || Controls.Keyboard.IsKeyDown(Key.Escape) ||
            !Windows.TryGetValue(DragTabId, out var source) || !source.Visible)
        {
            CancelTabDrag();
            return true;
        }
        if (Vector2.Distance(mouse, DragTabStart) >= 8) DragTabMoved = true;
        DropTabId = null;
        if (DragTabMoved)
            foreach (var tab in DockTabs)
                if (tab.Window.Id != DragTabId && tab.Bounds.Contains(mouse))
                {
                    DropTabId = tab.Window.Id;
                    DropTabAfter = mouse.X >= tab.Bounds.Center.X;
                    break;
                }
        if (!held)
        {
            if (DropTabId != null && Windows.TryGetValue(DropTabId, out var target))
            {
                DockTabOrder.Remove(DragTabId);
                int index = DockTabOrder.IndexOf(DropTabId) + (DropTabAfter ? 1 : 0);
                DockTabOrder.Insert(index, DragTabId);
                DockAssignments[DragTabId] = DockFor(target);
                ActivateDock(source);
                ComputeDockLayout();
                UpdatePresentationViewport();
            }
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
        Renderer.DrawSprite(solid, new Rectangle(GamePane.X, GamePane.Y, GamePane.Width, TitleBarHeight), OpaqueWorkspaceColor(TitleBarColor));
        DrawTextBold("Game", new Vector2(GamePane.X + Padding, GamePane.Y + 12), TextColor);
        Renderer.DrawSprite(WorkspaceSnapshot, GameViewport, Color.White);
        Renderer.EndDraw();
        foreach (var window in RenderOrder)
        {
            if (!IsWindowDisplayed(window)) continue;
            Renderer.SetTarget(DockSurface).Clear(OpaqueWorkspaceColor(WindowBg));
            Renderer.BeginDraw(transform: scale);
            DrawWindow(window, mouse, OpenMenuId != null || OpenDropdownWindowId != null);
            Renderer.EndDraw();
            Renderer.SetTarget(null);
            var pixels = WorkspacePixels(window.WindowBounds);
            Renderer.BeginDraw();
            Renderer.DrawSprite(DockSurface, pixels, pixels, Color.White);
            Renderer.EndDraw();
        }
        Renderer.BeginDraw(transform: scale);
        foreach (var tab in DockTabs)
        {
            Renderer.DrawSprite(solid, tab.Bounds, OpaqueWorkspaceColor(IsWindowDisplayed(tab.Window) ? ButtonHover : TitleBarColor));
            var close = DockTabCloseBounds(tab.Bounds);
            var (label, size) = FitMenuItemText(tab.Window.Title, close.X - tab.Bounds.X - 20);
            Renderer.DrawString("Inter", size, label, new Vector2(tab.Bounds.X + 10, tab.Bounds.Y + 8), IsWindowDisplayed(tab.Window) ? TextColor : LabelDim);
            bool closeHovered = OpenMenuId == null && OpenDropdownWindowId == null && close.Contains(mouse);
            Renderer.DrawRoundedRect(close, closeHovered ? CloseHover : CloseColor, CornerRadius);
            const float closeSize = FontSize * 0.8f;
            var textSize = Renderer.MeasureString("Inter-Bold", closeSize, "X");
            Renderer.DrawString("Inter-Bold", closeSize, "X", new Vector2(
                close.X + (close.Width - textSize.X) / 2,
                close.Y + (close.Height - textSize.Y) / 2 - 2), CloseText, bold: true);
        }
        if (DragTabMoved && DropTabId != null)
            foreach (var tab in DockTabs)
                if (tab.Window.Id == DropTabId)
                    Renderer.DrawSprite(solid, new Rectangle(
                        (DropTabAfter ? tab.Bounds.Right : tab.Bounds.Left) - 2,
                        tab.Bounds.Top + 3, 4, tab.Bounds.Height - 6), TextColor);
        if (GamePane.X > 0) Renderer.DrawSprite(solid, new Rectangle(GamePane.X - 1, MenuBarHeight, 2, GamePane.Height), ButtonHover);
        if (GamePane.Right < Renderer.VirtualWidth / UIScale) Renderer.DrawSprite(solid, new Rectangle(GamePane.Right - 1, MenuBarHeight, 2, GamePane.Height), ButtonHover);
        DrawDropdownPopup(mouse);
        DrawMenuBar(mouse);
        DrawMenuDropdown(mouse);
        DrawWorkspaceSubmenu(mouse);
        Renderer.EndDraw();
    }
}
