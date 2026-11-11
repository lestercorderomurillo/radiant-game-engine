using System;
using System.Collections.Generic;
using System.Linq;
using com.radiant.engine.core;
using com.radiant.engine.input;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private string PanelMenuWindowId;
    private Rectangle PanelMenuBounds;
    private int PanelMenuSelection;
    private static readonly string[] PanelActions = ["Move left", "Move right", "Move below preview", "Reset panel sizes", "Close panel"];
    // About uses a 28 screen-pixel square. Convert to Inspector coordinates, bounded by the header.
    private int ChromeButtonExtent => Math.Clamp((int)MathF.Round(CloseButtonSize * Renderer.VirtualWidth / (Renderer.ScreenWidth * UIScale)), 12, TitleBarHeight - InlineGap);
    private int DockHeaderHeight => TitleBarHeight;

    private Rectangle DockTabCloseBounds(Rectangle tab) => new(
        tab.Right - ChromeButtonExtent - 6, tab.Y + (tab.Height - ChromeButtonExtent) / 2,
        ChromeButtonExtent, ChromeButtonExtent);

    private Rectangle DockTabMenuBounds(Rectangle tab)
    {
        var close = DockTabCloseBounds(tab);
        return new Rectangle(close.X - ChromeButtonExtent - 4, close.Y, ChromeButtonExtent, ChromeButtonExtent);
    }

    private void DrawChromeButton(Rectangle bounds, bool hovered, bool close)
    {
        var solid = Renderer.GetSolidTexture(Color.White);
        Renderer.DrawSprite(solid, bounds, close ? (hovered ? CloseHover : CloseColor) : (hovered ? ButtonHover : TitleBarColor));
        if (close)
        {
            int length = Math.Max(6, (int)(bounds.Width * 0.45f));
            int thickness = Math.Max(1, (int)MathF.Round(bounds.Width * (1.5f / CloseButtonSize)));
            var stroke = new Rectangle(bounds.Center.X, bounds.Center.Y, length, thickness);
            Renderer.DrawSprite(solid, stroke, null, CloseText, MathF.PI / 4, new Vector2(0.5f));
            Renderer.DrawSprite(solid, stroke, null, CloseText, -MathF.PI / 4, new Vector2(0.5f));
        }
        else
        {
            int size = Math.Max(2, bounds.Width / 12);
            int spacing = Math.Max(4, bounds.Width / 5);
            for (int i = -1; i <= 1; i++)
                Renderer.DrawSprite(solid, new Rectangle(bounds.Center.X + i * spacing - size / 2, bounds.Center.Y - size / 2, size, size), TextColor);
        }
    }

    private void ClosePanelMenu() => PanelMenuWindowId = null;

    private void OpenPanelMenu(WindowData window, Rectangle anchor)
    {
        CancelTabDrag();
        CloseMenuDropdown();
        CloseDropdown();
        ClearTextInputFocus();
        PanelMenuWindowId = window.Id;
        PanelMenuSelection = -1;
        int width = Math.Min(320, (int)(Renderer.VirtualWidth / UIScale));
        int height = PanelActions.Length * MenuItemHeight;
        int screenHeight = (int)(Renderer.VirtualHeight / UIScale);
        int x = Math.Clamp(anchor.Right - width, 0, Math.Max(0, (int)(Renderer.VirtualWidth / UIScale) - width));
        int y = anchor.Bottom + height <= screenHeight ? anchor.Bottom : anchor.Top - height;
        PanelMenuBounds = new Rectangle(x, Math.Max(MenuBarHeight, y), width, height);
    }

    private bool UpdatePanelMenu(Vector2 mouse, bool pressed, KeyboardState keyboard)
    {
        if (PanelMenuWindowId == null) return false;
        MouseOverUI = true;
        if (!Renderer.IsActive || keyboard.IsKeyDown(Key.Escape) ||
            !Windows.TryGetValue(PanelMenuWindowId, out var window) || !window.Visible)
        { ClosePanelMenu(); return true; }
        if (keyboard.IsKeyDown(Key.Down) && PrevKeyState.IsKeyUp(Key.Down))
            PanelMenuSelection = (PanelMenuSelection + 1) % PanelActions.Length;
        if (keyboard.IsKeyDown(Key.Up) && PrevKeyState.IsKeyUp(Key.Up))
            PanelMenuSelection = PanelMenuSelection < 0 ? PanelActions.Length - 1 : (PanelMenuSelection + PanelActions.Length - 1) % PanelActions.Length;
        if (keyboard.IsKeyDown(Key.Enter) && PrevKeyState.IsKeyUp(Key.Enter) && PanelMenuSelection >= 0)
        { ApplyPanelAction(window, PanelMenuSelection); return true; }
        if (pressed)
        {
            if (PanelMenuBounds.Contains(mouse)) ApplyPanelAction(window, ((int)mouse.Y - PanelMenuBounds.Y) / MenuItemHeight);
            else ClosePanelMenu();
        }
        return true;
    }

    private void ApplyPanelAction(WindowData window, int action)
    {
        ClosePanelMenu();
        if (action < 3) MovePanel(window, action);
        else if (action == 3)
        {
            LeftDockFraction = RightDockFraction = 0.26f;
            BottomDockFraction = 0.30f;
            DockStackHeights.Clear();
        }
        else if (action == 4) SetWindowVisible(window.Id, false);
        ComputeDockLayout();
        UpdatePresentationViewport();
        SyncViewToggleValues();
    }

    private void MovePanel(WindowData window, int dock)
    {
        DockStackHeights.Clear();
        DockAssignments[window.Id] = dock;
        ActivateDock(window);
        DockScroll[window.Id] = 0;
    }

    private void DrawPanelMenu(Vector2 mouse)
    {
        if (PanelMenuWindowId == null || !Windows.TryGetValue(PanelMenuWindowId, out var window)) return;
        var solid = Renderer.GetSolidTexture(Color.White);
        Renderer.DrawSprite(solid, new Rectangle(PanelMenuBounds.X - 1, PanelMenuBounds.Y - 1, PanelMenuBounds.Width + 2, PanelMenuBounds.Height + 2), LabelDim);
        Renderer.DrawSprite(solid, PanelMenuBounds, OpaqueWorkspaceColor(WindowBg));
        for (int i = 0; i < PanelActions.Length; i++)
        {
            var row = new Rectangle(PanelMenuBounds.X, PanelMenuBounds.Y + i * MenuItemHeight, PanelMenuBounds.Width, MenuItemHeight);
            bool current = i < 3 && DockFor(window) == i;
            if (row.Contains(mouse) || PanelMenuSelection == i) Renderer.DrawSprite(solid, row, ButtonHover);
            var (text, size) = FitMenuItemText(PanelActions[i] + (current ? " (current)" : ""), row.Width - 24);
            var measured = Renderer.MeasureString("Inter", size, text);
            Renderer.DrawString("Inter", size, text, new Vector2(row.X + 12, row.Y + (row.Height - measured.Y) / 2), current ? LabelDim : TextColor);
        }
    }

    private void ApplyWindowScroll(WindowData window, int bottomLimit)
    {
        int bottom = window.Widgets.Where(w => w.Visible).Select(w => w.Bounds.Bottom).DefaultIfEmpty((int)window.Position.Y).Max() + (Padding);
        int scroll = Math.Clamp(DockScroll.GetValueOrDefault(window.Id), 0, Math.Max(0, bottom - bottomLimit));
        DockScroll[window.Id] = scroll;
        if (scroll == 0) return;
        for (int i = 0; i < window.Widgets.Count; i++)
        {
            var widget = window.Widgets[i];
            widget.Bounds = new Rectangle(widget.Bounds.X, widget.Bounds.Y - scroll, widget.Bounds.Width, widget.Bounds.Height);
            window.Widgets[i] = widget;
        }
    }

    private bool UpdateWindowScroll(Vector2 mouse, int scroll)
    {
        if (scroll == 0 || OpenDropdownWindowId != null) return false;
        for (int i = RenderOrder.Count - 1; i >= 0; i--)
        {
            var window = RenderOrder[i];
            if (!IsWindowDisplayed(window) || !window.WindowBounds.Contains(mouse)) continue;
            if (window.Widgets.Any(w => w.Visible && w.Type == WidgetType.ListBox && w.Bounds.Contains(mouse))) return false;
            DockScroll[window.Id] = Math.Max(0, DockScroll.GetValueOrDefault(window.Id) - Math.Sign(scroll) * 90);
            ComputeAllLayouts();
            return true;
        }
        return false;
    }

    private void EnsureWindowSurface()
    {
        if (DockSurface != null && DockSurface.Width == Renderer.ScreenWidth && DockSurface.Height == Renderer.ScreenHeight) return;
        DockSurface?.Dispose();
        DockSurface = Renderer.CreateRenderTarget(Renderer.ScreenWidth, Renderer.ScreenHeight);
    }

    private void DrawClippedWindow(WindowData window, Vector2 mouse, bool blocked, Matrix scale)
    {
        EnsureWindowSurface();
        Renderer.SetTarget(DockSurface).Clear(Color.Transparent);
        Renderer.BeginDraw(transform: scale);
        DrawWindow(window, mouse, blocked);
        Renderer.EndDraw();
        Renderer.SetTarget(null);
        var rect = WorkspacePixels(window.WindowBounds);
        int left = Math.Max(0, rect.Left), top = Math.Max(0, rect.Top);
        int right = Math.Min(Renderer.ScreenWidth, rect.Right), bottom = Math.Min(Renderer.ScreenHeight, rect.Bottom);
        if (right <= left || bottom <= top) return;
        var pixels = new Rectangle(left, top, right - left, bottom - top);
        Renderer.BeginDraw();
        Renderer.DrawSprite(DockSurface, pixels, pixels, Color.White);
        Renderer.EndDraw();
    }
}
