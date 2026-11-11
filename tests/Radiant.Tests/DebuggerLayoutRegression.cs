using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.input;

internal static class DebuggerLayoutRegression
{
    public static void Run(IRenderer renderer)
    {
        var scene = new Scene { Renderer = renderer };
        scene.Initialize();
        try
        {
            var inspector = scene.ECS.AddSystem<Inspector>();
            inspector.Initialize();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            object Field(string name) => typeof(Inspector).GetField(name, flags).GetValue(inspector);
            object Call(string name, params object[] arguments) => typeof(Inspector).GetMethod(name, flags).Invoke(inspector, arguments);
            inspector.SetWorkspaceMode(WorkspaceMode.Panels);
            var windows = (Dictionary<string, WindowData>)Field("Windows");
            var stack = new List<WindowData>();
            foreach (string id in new[] { "stack_a", "stack_b", "stack_c" })
            {
                Inspector.CreateWindow(id, id);
                Inspector.AddButton(id, "button", "Action", () => { });
                stack.Add(windows[id]);
            }
            var displayed = (HashSet<string>)Field("DisplayedDockWindows");
            displayed.Clear();
            Call("LayoutDockStack", stack, 0, 44, 600, 900, 1);
            if (stack.Count(w => displayed.Contains(w.Id)) != 3) throw new Exception("Panels were tabbed despite sufficient height.");
            for (int i = 1; i < stack.Count; i++)
                if (stack[i - 1].WindowBounds.Bottom > stack[i].WindowBounds.Top) throw new Exception("Stacked panel bodies overlap.");
            displayed.Clear();
            Call("LayoutDockStack", stack, 0, 44, 600, 450, 1);
            if (stack.Count(w => displayed.Contains(w.Id)) != 2) throw new Exception("Overflow did not preserve an independent panel beside the tab group.");
            foreach (var panel in stack) Inspector.DestroyWindow(panel.Id);
            Call("ComputeDockLayout");
            var game = (Rectangle)Field("GamePane");
            var bottom = (Rectangle)Field("BottomPane");
            if (bottom.Height <= 0 || bottom.Y != game.Bottom || bottom.X != game.X || bottom.Width != game.Width)
                throw new Exception("Debugger does not fill the area below Game.");
            var tabs = (List<(WindowData Window, Rectangle Bounds)>)Field("DockTabs");
            if (tabs.Single(tab => tab.Window.Id == "debugger").Bounds.Width != bottom.Width)
                throw new Exception("Single debugger tab does not fill its pane.");
            EngineLog.Write("Debugger layout regression");
            Call("UpdateDebugger");
            var debugger = windows["debugger"];
            if (!debugger.Widgets[debugger.WidgetIndex["log"]].ListBoxItems.Any(line => line.Contains("Debugger layout regression")))
                throw new Exception("Debugger did not receive engine logs.");
            Inspector.HideWindow("debugger");
            Call("ComputeDockLayout");
            if (((Rectangle)Field("GamePane")).Height != game.Height + bottom.Height)
                throw new Exception("Closing Debugger did not expand Game.");
            Inspector.ShowWindow("debugger");
            Call("ComputeDockLayout");
            if (((Rectangle)Field("BottomPane")) != bottom)
                throw new Exception("Reopening Debugger did not restore its dock.");
            inspector.SetWorkspaceMode(WorkspaceMode.Windows);
            if (renderer.ScenePresentationBounds != null) throw new Exception("Floating mode retained docked input mapping.");
            inspector.SetWorkspaceMode(WorkspaceMode.Panels);
            if (((Rectangle)Field("BottomPane")) != bottom)
                throw new Exception("Workspace switching lost the bottom dock.");
            // Toolbar sizing and filters must still work with the smaller bottom pane.
            var clear = debugger.Widgets[debugger.WidgetIndex["clear"]];
            var log = debugger.Widgets[debugger.WidgetIndex["log"]];
            if (clear.Bounds.Width >= debugger.WindowBounds.Width / 2 || log.Bounds.Bottom > debugger.WindowBounds.Bottom)
                throw new Exception("Debugger toolbar is oversized or hides the log.");
            EngineLog.Write("Needle warning", LogLevel.Warning);
            EngineLog.Write("Needle error", LogLevel.Error);
            var severity = debugger.Widgets[debugger.WidgetIndex["level"]];
            Call("OpenDropdown", "debugger", "level", severity);
            var popup = (Rectangle)Field("OpenDropdownPopupBounds");
            if (popup.Bottom > severity.Bounds.Top) throw new Exception("Bottom toolbar dropdown did not open upward.");
            var errorOption = new Vector2(popup.Center.X, popup.Bottom - popup.Height / 8);
            if ((bool)Call("UpdateDockInput", errorOption, true, 0)) throw new Exception("Panel input intercepted an open dropdown.");
            Call("HandleDropdownPopupClick", errorOption);
            if (debugger.Widgets[debugger.WidgetIndex["level"]].DropdownSelected != 3 || Field("OpenDropdownWindowId") != null)
                throw new Exception("Upward dropdown did not select its last option.");
            debugger.Widgets[debugger.WidgetIndex["search"]].TextInputCallback("NEEDLE");
            debugger.Widgets[debugger.WidgetIndex["level"]].DropdownCallback(3);
            Call("UpdateDebugger");
            var rows = debugger.Widgets[debugger.WidgetIndex["log"]].ListBoxItems;
            if (rows.Length != 1 || !rows[0].Contains("Needle error")) throw new Exception("Search and severity filters do not combine.");
            clear.ButtonCallback();
            Call("UpdateDebugger");
            if (debugger.Widgets[debugger.WidgetIndex["log"]].ListBoxItems.Length != 0) throw new Exception("Clear did not empty the filtered log.");

            // The menu moves a panel without dragging, including into an empty dock.
            var entity = windows["entity_inspector"];
            Call("ApplyPanelAction", entity, 1);
            if ((int)Call("DockFor", entity) != 1 || ((Rectangle)Field("GamePane")).X != 0)
                throw new Exception("Menu move did not empty the original dock.");
            var entityTab = tabs.Single(t => t.Window == entity);
            var menu = (Rectangle)Call("DockTabMenuBounds", entityTab.Bounds);
            var close = (Rectangle)Call("DockTabCloseBounds", entityTab.Bounds);
            if (close.Width != close.Height || menu.Intersects(close)) throw new Exception("Header buttons are not separate squares.");
            Call("UpdateDockInput", new Vector2(menu.Center.X, menu.Center.Y), true, 0);
            if ((string)Field("PanelMenuWindowId") != entity.Id || Field("DragTabId") != null) throw new Exception("Ellipsis started dragging instead of opening its menu.");
            Call("UpdatePanelMenu", Vector2.Zero, false, new KeyboardState(Key.Escape));
            if (Field("PanelMenuWindowId") != null) throw new Exception("Escape did not dismiss panel menu.");
            Call("OpenPanelMenu", entity, entityTab.Bounds);
            Call("UpdatePanelMenu", Vector2.Zero, false, new KeyboardState(Key.Up));
            if ((int)Field("PanelMenuSelection") != 4) throw new Exception("Up did not start keyboard menu navigation at the last item.");
            Call("UpdatePanelMenu", Vector2.Zero, true, default(KeyboardState));
            if (Field("PanelMenuWindowId") != null || !entity.Visible) throw new Exception("Outside click did not dismiss safely.");

            // Hold, preview, and release over the empty left edge, not another tab.
            var start = new Vector2(entityTab.Bounds.X + 15, entityTab.Bounds.Y + 20);
            Call("UpdateDockInput", start, true, 0);
            var end = new Vector2(20, 220);
            Call("UpdateDockTabDrag", end, true);
            if ((int)Field("DropDock") != 0 || !(bool)Field("DragTabMoved") || Field("DragTabId") == null)
                throw new Exception("Held drag did not expose an empty-dock preview.");
            Call("UpdateDockTabDrag", end, false);
            if ((int)Call("DockFor", entity) != 0 || Field("DragTabId") != null) throw new Exception("Empty-side drop did not complete.");
            entityTab = tabs.Single(t => t.Window == entity);
            Call("UpdateDockInput", new Vector2(entityTab.Bounds.X + 15, entityTab.Bounds.Y + 20), true, 0);
            scene.Controls = new InputFrame(new InputSnapshot(new KeyboardState(Key.Escape), default), default);
            Call("UpdateDockTabDrag", new Vector2(1000, 220), true);
            if (Field("DragTabId") != null || (int)Call("DockFor", entity) != 0) throw new Exception("Escape committed a drag.");

            // Resize the left and bottom splits through the actual drag input handler.
            scene.Controls = new InputFrame(new InputSnapshot(default, new MouseState(LeftButton: ButtonState.Pressed)), default);
            game = (Rectangle)Field("GamePane");
            Call("UpdateDockInput", new Vector2(game.X, 240), true, 0);
            Call("UpdateDockInput", new Vector2(game.X + 60, 240), false, 0);
            if (((Rectangle)Field("GamePane")).X <= game.X) throw new Exception("Left divider failed to resize.");
            scene.Controls = new InputFrame(new InputSnapshot(default, new MouseState()), default);
            Call("UpdateDockInput", Vector2.Zero, false, 0);
            bottom = (Rectangle)Field("BottomPane");
            scene.Controls = new InputFrame(new InputSnapshot(default, new MouseState(LeftButton: ButtonState.Pressed)), default);
            Call("UpdateDockInput", new Vector2(bottom.Center.X, bottom.Y), true, 0);
            Call("UpdateDockInput", new Vector2(bottom.Center.X, bottom.Y - 60), false, 0);
            if (((Rectangle)Field("BottomPane")).Height <= bottom.Height) throw new Exception("Bottom divider failed to resize.");
            scene.Controls = InputFrame.Empty;
            Call("UpdateDockInput", Vector2.Zero, false, 0);

            // All floating windows resize from their corner, including a previously fixed panel.
            inspector.SetWorkspaceMode(WorkspaceMode.Windows);
            foreach (var window in windows.Values) window.Visible = window.Id == "inspector";
            var floating = windows["inspector"];
            floating.Position = new Vector2(20, 100);
            floating.Size = new Vector2(500, 0);
            floating.ResizedHeight = 400;
            Call("ComputeAllLayouts");
            if (!floating.Resizable) throw new Exception("Default window does not allow corner resizing.");
            typeof(Inspector).GetField("GlobalVisible", flags).SetValue(inspector, true);
            var corner = (Rectangle)Call("WorkspacePixels", new Rectangle(floating.WindowBounds.Right - 8, floating.WindowBounds.Bottom - 8, 1, 1));
            scene.Controls = new InputFrame(new InputSnapshot(default, new MouseState(corner.X, corner.Y, LeftButton: ButtonState.Pressed)), default);
            inspector.Update();
            if (!(bool)Field("Resizing")) throw new Exception("Corner press did not begin resize.");
            scene.Controls = new InputFrame(new InputSnapshot(default, new MouseState(corner.X + 20, corner.Y + 20, LeftButton: ButtonState.Pressed)), default);
            inspector.Update();
            if (floating.Size.X <= 500 || floating.ResizedHeight <= 400) throw new Exception("Corner drag did not resize both dimensions.");
            scene.Controls = new InputFrame(new InputSnapshot(default, new MouseState(corner.X + 20, corner.Y + 20)), default);
            inspector.Update();
            if ((bool)Field("Resizing")) throw new Exception("Corner resize did not release.");
            Console.WriteLine("PASS: Debugger filters and toolbar, menu moves, empty-side drag preview, Escape, split resizing, and floating corner resizing.");
        }
        finally { scene.Dispose(); EngineLog.Clear(); }
    }
}
