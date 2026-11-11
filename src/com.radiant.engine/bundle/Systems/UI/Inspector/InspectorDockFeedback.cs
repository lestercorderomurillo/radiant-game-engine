using System;
using System.Linq;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private int DockDropTarget(Vector2 mouse)
    {
        int width = (int)(Renderer.VirtualWidth / UIScale);
        int height = (int)(Renderer.VirtualHeight / UIScale);
        if (mouse.X < 0 || mouse.X >= width || mouse.Y < MenuBarHeight || mouse.Y >= height) return -1;
        if (mouse.X < (GamePane.X > 0 ? GamePane.X : width * 0.18f)) return 0;
        if (mouse.X >= (GamePane.Right < width ? GamePane.Right : width * 0.82f)) return 1;
        if (mouse.Y >= (BottomPane.Height > 0 ? BottomPane.Y : height - (height - MenuBarHeight) * 0.22f)) return 2;
        return -1;
    }

    private Rectangle DockDropPreview(int target)
    {
        int width = (int)(Renderer.VirtualWidth / UIScale);
        int height = (int)(Renderer.VirtualHeight / UIScale);
        bool Occupied(int dock) => target == dock || Windows.Values.Any(w => w.Visible && w.Id != DragTabId && DockFor(w) == dock);
        int left = Occupied(0) ? (int)(width * LeftDockFraction) : 0;
        int right = Occupied(1) ? (int)(width * RightDockFraction) : 0;
        int bottom = Occupied(2) ? ComputeBottomDockHeight(height - MenuBarHeight) : 0;
        return target switch
        {
            0 => new Rectangle(0, MenuBarHeight, left, height - MenuBarHeight),
            1 => new Rectangle(width - right, MenuBarHeight, right, height - MenuBarHeight),
            _ => new Rectangle(left, height - bottom, Math.Max(1, width - left - right), bottom)
        };
    }

    private void DrawDockDragPreview()
    {
        if (DragTabId == null || !Windows.TryGetValue(DragTabId, out var window)) return;
        var solid = Renderer.GetSolidTexture(Color.White);
        if (DropDock >= 0)
        {
            var bounds = DockDropPreview(DropDock);
            Renderer.DrawSprite(solid, bounds, TextColor, 0.12f);
            Renderer.DrawSprite(solid, new Rectangle(bounds.X, bounds.Y, bounds.Width, 3), TextColor);
            Renderer.DrawSprite(solid, new Rectangle(bounds.X, bounds.Bottom - 3, bounds.Width, 3), TextColor);
            Renderer.DrawSprite(solid, new Rectangle(bounds.X, bounds.Y, 3, bounds.Height), TextColor);
            Renderer.DrawSprite(solid, new Rectangle(bounds.Right - 3, bounds.Y, 3, bounds.Height), TextColor);
        }
        string destination = DropDock switch { 0 => "Left", 1 => "Right", 2 => "Below Game", _ => "Move panel" };
        int width = Math.Min(360, (int)(Renderer.VirtualWidth / UIScale));
        int height = DockHeaderHeight + 34;
        int x = Math.Clamp((int)DragTabMouse.X + 20, 0, Math.Max(0, (int)(Renderer.VirtualWidth / UIScale) - width));
        int y = Math.Clamp((int)DragTabMouse.Y + 18, MenuBarHeight, Math.Max(MenuBarHeight, (int)(Renderer.VirtualHeight / UIScale) - height));
        var ghost = new Rectangle(x, y, width, height);
        Renderer.DrawSprite(solid, new Rectangle(x - 1, y - 1, width + 2, height + 2), TextColor);
        Renderer.DrawSprite(solid, ghost, OpaqueWorkspaceColor(TitleBarColor));
        var (label, size) = FitMenuItemText(window.Title, width - 24);
        Renderer.DrawString("Inter", size, label, new Vector2(x + 12, y + 8), TextColor);
        Renderer.DrawString("Inter", FontSize * 0.8f, destination, new Vector2(x + 12, y + DockHeaderHeight), LabelDim);
    }

    private void DrawDockDividers(Vector2 mouse)
    {
        var solid = Renderer.GetSolidTexture(Color.White);
        int height = GamePane.Height + BottomPane.Height;
        bool canHover = PanelMenuWindowId == null && OpenMenuId == null && OpenDropdownWindowId == null && DragTabId == null;
        void Vertical(int x, int dock)
        {
            bool active = DraggingDock == dock || (canHover && mouse.Y > MenuBarHeight && Math.Abs(mouse.X - x) <= 10);
            Renderer.DrawSprite(solid, new Rectangle(x - 2, MenuBarHeight, 4, height), active ? TextColor : ButtonHover);
            int y = MenuBarHeight + height / 2;
            for (int i = -1; i <= 1; i++) Renderer.DrawSprite(solid, new Rectangle(x - 3, y + i * 10, 6, 3), active ? TextColor : LabelDim);
        }
        if (GamePane.X > 0) Vertical(GamePane.X, 0);
        if (GamePane.Right < Renderer.VirtualWidth / UIScale) Vertical(GamePane.Right, 1);
        if (BottomPane.Height > 0)
        {
            bool active = DraggingDock == 2 || (canHover && mouse.X >= BottomPane.X && mouse.X < BottomPane.Right && Math.Abs(mouse.Y - BottomPane.Y) <= 10);
            Renderer.DrawSprite(solid, new Rectangle(BottomPane.X, BottomPane.Y - 2, BottomPane.Width, 4), active ? TextColor : ButtonHover);
            for (int i = -1; i <= 1; i++) Renderer.DrawSprite(solid, new Rectangle(BottomPane.Center.X + i * 10, BottomPane.Y - 3, 3, 6), active ? TextColor : LabelDim);
        }
    }
}
