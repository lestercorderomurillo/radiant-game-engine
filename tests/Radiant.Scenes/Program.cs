using System;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Radiant.Testing;
using Microsoft.Xna.Framework;

if (Array.IndexOf(args, "--workspace-regression") >= 0)
{
    const global::System.Reflection.BindingFlags privateInstance = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance;
    object Field(Inspector inspector, string name) => typeof(Inspector).GetField(name, privateInstance).GetValue(inspector);
    object Invoke(Inspector inspector, string name, params object[] values) => typeof(Inspector).GetMethod(name, privateInstance).Invoke(inspector, values);
    Vector2 floatingPosition = default, floatingSize = default;
    void SelectMode(Inspector inspector, WorkspaceMode mode)
    {
        Invoke(inspector, "OpenMenuDropdown", "view");
        var bounds = (Rectangle)Field(inspector, "OpenMenuDropdownBounds");
        Invoke(inspector, "UpdateMenuBar", new Vector2(bounds.X + 20, bounds.Y + 20), false, default(com.radiant.engine.input.KeyboardState));
        var submenu = (Rectangle)Field(inspector, "WorkspaceSubmenuBounds");
        Invoke(inspector, "UpdateMenuBar", new Vector2(submenu.X + 20, submenu.Y + 24 + (int)mode * 48), true, default(com.radiant.engine.input.KeyboardState));
        if (inspector.CurrentWorkspaceMode != mode || Field(inspector, "OpenMenuId") != null) throw new Exception("Workspace submenu selection failed.");
    }
    using var window = new SmokeWindow(new SimpleLightScene(), "artifacts/workspace", (scene, frame) =>
    {
        var inspector = scene.ECS.GetSystem<Inspector>();
        var windows = (global::System.Collections.Generic.Dictionary<string, WindowData>)Field(inspector, "Windows");
        if (frame == 1)
        {
            floatingPosition = windows["inspector"].Position;
            floatingSize = windows["inspector"].Size;
            typeof(Inspector).GetField("GlobalVisible", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance).SetValue(inspector, true);
            SelectMode(inspector, WorkspaceMode.Panels);
        }
        if (frame == 30)
        {
            SelectMode(inspector, WorkspaceMode.Windows);
            if (windows["inspector"].Position != floatingPosition || windows["inspector"].Size != floatingSize) throw new Exception("Floating layout was not restored.");
            if (scene.Renderer.ScenePresentationBounds != null) throw new Exception("Windows mode retained viewport input mapping.");
        }
        if (frame == 35) SelectMode(inspector, WorkspaceMode.Panels);
        if (frame == 40) Inspector.ShowWindow("inspector");
        if (frame == 42)
        {
            var game = (Rectangle)Field(inspector, "GamePane");
            var bottom = (Rectangle)Field(inspector, "BottomPane");
            if (bottom.Height <= 0 || bottom.Y != game.Bottom || bottom.X != game.X || bottom.Width != game.Width)
                throw new Exception("Debugger is not docked below Game.");
            if (windows["debugger"].WindowBounds.Y < bottom.Y)
                throw new Exception("Debugger content overlaps Game.");
            Inspector.HideWindow("debugger");
            Invoke(inspector, "ComputeDockLayout");
            if (((Rectangle)Field(inspector, "GamePane")).Height != game.Height + bottom.Height)
                throw new Exception("Closing Debugger did not return its space to Game.");
            Inspector.ShowWindow("debugger");
            Invoke(inspector, "ComputeDockLayout");
        }
        if (frame == 45)
        {
            Invoke(inspector, "OpenMenuDropdown", "view");
            var bounds = (Rectangle)Field(inspector, "OpenMenuDropdownBounds");
            Invoke(inspector, "UpdateMenuBar", new Vector2(bounds.X + 20, bounds.Y + 20), false, default(com.radiant.engine.input.KeyboardState));
        }
        if (frame == 46)
        {
            using var capture = global::System.IO.File.Create("artifacts/workspace/menu.png");
            scene.Renderer.SceneRT.SaveAsPng(capture, scene.Renderer.ScreenWidth, scene.Renderer.ScreenHeight);
            Invoke(inspector, "UpdateMenuBar", Vector2.Zero, false, new com.radiant.engine.input.KeyboardState(com.radiant.engine.input.Key.Escape));
            if (Field(inspector, "OpenMenuId") != null) throw new Exception("Escape did not dismiss the workspace submenu.");
        }
        if (frame == 50 || frame == 55)
        {
            var tabs = (global::System.Collections.Generic.List<(WindowData Window, Rectangle Bounds)>)Field(inspector, "DockTabs");
            var source = tabs.Find(tab => tab.Window.Id == "inspector");
            var target = frame == 50 ? tabs.FindLast(tab => tab.Window.Id != "inspector" && tab.Bounds.X >= source.Bounds.X)
                : tabs.Find(tab => tab.Window.Id == "entity_inspector");
            var start = new Vector2(source.Bounds.X + 15, source.Bounds.Y + 20);
            var end = new Vector2(target.Bounds.Right - 10, target.Bounds.Y + 20);
            Invoke(inspector, "UpdateDockInput", start, true, 0);
            Invoke(inspector, "UpdateDockTabDrag", end, true);
            Invoke(inspector, "UpdateDockTabDrag", end, false);
            var order = (global::System.Collections.Generic.List<string>)Field(inspector, "DockTabOrder");
            if (order.IndexOf("inspector") != order.IndexOf(target.Window.Id) + 1)
                throw new Exception("Tab drag failed to insert after the destination.");
            if (frame == 55)
            {
                if ((int)Invoke(inspector, "DockFor", windows["inspector"]) != (int)Invoke(inspector, "DockFor", windows["entity_inspector"]))
                    throw new Exception("Tab drag did not move to the other dock.");
                SelectMode(inspector, WorkspaceMode.Windows);
                SelectMode(inspector, WorkspaceMode.Panels);
                if ((int)Invoke(inspector, "DockFor", windows["inspector"]) != (int)Invoke(inspector, "DockFor", windows["entity_inspector"]))
                    throw new Exception("Workspace switch lost the tab's dock.");
            }
        }
        void CaptureReview(string name)
        {
            global::System.IO.Directory.CreateDirectory("artifacts/workspace-review/after");
            using var capture = global::System.IO.File.Create($"artifacts/workspace-review/after/{name}.png");
            scene.Renderer.SceneRT.SaveAsPng(capture, scene.Renderer.ScreenWidth, scene.Renderer.ScreenHeight);
        }
        if (frame == 31) CaptureReview("floating");
        if (frame == 60)
        {
            var tabs = (global::System.Collections.Generic.List<(WindowData Window, Rectangle Bounds)>)Field(inspector, "DockTabs");
            var tab = tabs.Find(t => t.Window.Id == "entity_inspector");
            var menuButton = (Rectangle)Invoke(inspector, "DockTabMenuBounds", tab.Bounds);
            Invoke(inspector, "UpdateDockInput", new Vector2(menuButton.Center.X, menuButton.Center.Y), true, 0);
            if ((string)Field(inspector, "PanelMenuWindowId") != "entity_inspector") throw new Exception("Panel menu did not open from ellipsis.");
        }
        if (frame == 61)
        {
            CaptureReview("panel-menu");
            Invoke(inspector, "ClosePanelMenu");
        }
        if (frame == 63)
        {
            CaptureReview("drag-preview");
            var end = (Vector2)Field(inspector, "DragTabMouse");
            Invoke(inspector, "UpdateDockTabDrag", end, false);
            if ((int)Invoke(inspector, "DockFor", windows["entity_inspector"]) != 1) throw new Exception("Drop on panel body did not move the entity inspector.");
        }
        if (frame == 64)
        {
            Invoke(inspector, "ApplyPanelAction", windows["entity_inspector"], 0);
            Invoke(inspector, "ApplyPanelAction", windows["inspector"], 1);
        }
        if (frame == 66)
        {
            var host = (com.radiant.engine.backends.monogame.MonoGameWindow)scene.Renderer.Window;
            host.GraphicsDeviceManager.PreferredBackBufferWidth = 1440;
            host.GraphicsDeviceManager.PreferredBackBufferHeight = 900;
            host.GraphicsDeviceManager.ApplyChanges();
        }
        if (frame == 70) CaptureReview("large");
        if (frame == 71)
        {
            CaptureReview("debugger-controls");
            Invoke(inspector, "CloseDropdown");
        }
        if (frame == 72)
        {
            var host = (com.radiant.engine.backends.monogame.MonoGameWindow)scene.Renderer.Window;
            host.GraphicsDeviceManager.PreferredBackBufferWidth = 1024;
            host.GraphicsDeviceManager.PreferredBackBufferHeight = 640;
            host.GraphicsDeviceManager.ApplyChanges();
            EngineLog.Write("Workspace regression: warning filter check", LogLevel.Warning, "UI test");
            EngineLog.Write("Workspace regression: error filter check", LogLevel.Error, "UI test");
            var debugger = windows["debugger"];
            Inspector.SetTextInputValue("debugger", "search", "filter check");
            debugger.Widgets[debugger.WidgetIndex["search"]].TextInputCallback("filter check");
            Inspector.SetDropdownValue("debugger", "level", 3);
            debugger.Widgets[debugger.WidgetIndex["level"]].DropdownCallback(3);
        }
        if (frame == 75)
        {
            CaptureReview("filtered-log");
            var debugger = windows["debugger"];
            if (debugger.Widgets[debugger.WidgetIndex["log"]].ListBoxItems.Length != 1) throw new Exception("Debugger filters failed in native renderer.");
            Inspector.SetTextInputValue("debugger", "search", "");
            debugger.Widgets[debugger.WidgetIndex["search"]].TextInputCallback("");
            Inspector.SetDropdownValue("debugger", "level", 0);
            debugger.Widgets[debugger.WidgetIndex["level"]].DropdownCallback(0);
        }
        if (frame == 76)
        {
            var debugger = windows["debugger"];
            Invoke(inspector, "OpenDropdown", "debugger", "level", debugger.Widgets[debugger.WidgetIndex["level"]]);
        }
        if (frame == 77)
        {
            CaptureReview("severity-menu");
            var popup = (Rectangle)Field(inspector, "OpenDropdownPopupBounds");
            Invoke(inspector, "HandleDropdownPopupClick", new Vector2(popup.Center.X, popup.Bottom - popup.Height / 8));
            var debugger = windows["debugger"];
            if (debugger.Widgets[debugger.WidgetIndex["level"]].DropdownSelected != 3) throw new Exception("Native severity popup selection failed.");
            Inspector.SetDropdownValue("debugger", "level", 0);
            debugger.Widgets[debugger.WidgetIndex["level"]].DropdownCallback(0);
        }
        if (frame > 35 && scene.Renderer.ScenePresentationBounds is not { Width: > 0, Height: > 0 })
            throw new Exception("Panels mode lost its viewport after resize.");
    }, (scene, frame) =>
    {
        if (frame == 70)
        {
            var activeInspector = scene.ECS.GetSystem<Inspector>();
            var panels = (global::System.Collections.Generic.Dictionary<string, WindowData>)Field(activeInspector, "Windows");
            var debugger = panels["debugger"];
            Invoke(activeInspector, "OpenDropdown", "debugger", "level", debugger.Widgets[debugger.WidgetIndex["level"]]);
            return;
        }
        if (frame != 62) return;
        var inspector = scene.ECS.GetSystem<Inspector>();
        var tabs = (global::System.Collections.Generic.List<(WindowData Window, Rectangle Bounds)>)Field(inspector, "DockTabs");
        var tab = tabs.Find(t => t.Window.Id == "entity_inspector");
        var game = (Rectangle)Field(inspector, "GamePane");
        var end = new Vector2(game.Right + 30, game.Y + 250);
        Invoke(inspector, "UpdateDockInput", new Vector2(tab.Bounds.X + 15, tab.Bounds.Y + 20), true, 0);
        Invoke(inspector, "UpdateDockTabDrag", end, true);
    });
    window.Run();
    return;
}

if (Array.IndexOf(args, "--startup-regression") >= 0)
{
    foreach (var Mode in new[] { "success", "failure", "cancel", "hidden" })
    {
        using var window = new StartupRegressionWindow(Mode);
        window.Run();
    }
    return;
}

if (Array.IndexOf(args, "--color-pipeline") >= 0)
{
    using var window = new ColorPipelineRegressionWindow();
    window.Run();
    return;
}

if (Array.IndexOf(args, "--udr4-regression") >= 0)
{
    using var window = new UDR4RegressionWindow();
    window.Run();
    return;
}

if (args.Length >= 2 && args[0] == "--lighting-captures")
{
    Scene captureScene = args.Length > 2 && args[2] == "triangles"
        ? new FrozenMegaLightsScene() : new LightingReferenceScene();
    using var window = new LightingCaptureWindow(captureScene, args[1]);
    window.Run();

    return;
}

if (Array.IndexOf(args, "--lighting-regression") >= 0)
{
    using var window = new LightingRegressionWindow();
    window.Run();
    return;
}

Scene Scene = args.Length == 0 || args[0] == "lights" ? new SimpleLightScene()
    : args[0] == "rendering2d" ? new Rendering2DScene()
    : args[0] == "tiles" ? new TilesetScene()
    : args[0] == "triangles" ? new MegaLightsScene()
    : throw new ArgumentException("Choose a test scene: lights, tiles, triangles, or rendering2d.");

if (Array.IndexOf(args, "--upscaling-regression") >= 0)
{
    using var window = new UpscalingRegressionWindow(Scene);
    window.Run();
}
else if (args.Length > 1 && args[1] == "--smoke")
{
    using var Window = new SmokeWindow(Scene, "artifacts/smoke", (ActiveScene, Frame) =>
    {
        if (ActiveScene is SimpleLightScene or MegaLightsScene)
        {
            var renderer = ActiveScene.Renderer;
            var center = renderer.VirtualSize / 2;
            var projectedCenter = renderer.WorldToScreen(center);
            float horizontalScale = Vector2.Distance(projectedCenter, renderer.WorldToScreen(center + Vector2.UnitX));
            float verticalScale = Vector2.Distance(projectedCenter, renderer.WorldToScreen(center + Vector2.UnitY));

            if (MathF.Abs(horizontalScale - verticalScale) > 0.001f || Vector2.Distance(projectedCenter, renderer.ScreenSize / 2) > 0.01f)
                throw new Exception("Lighting demo is stretched or off-center.");
            if (Vector2.Distance(renderer.ScreenToWorld(projectedCenter), center) > 0.01f)
                throw new Exception("Demo input mapping does not follow the fitted view.");
        }

        // The world begins with 100 rows of air. Frame the surface for captures
        // so the tiles smoke exercises terrain instead of only the sky clear color.
        if (Frame == 1 && ActiveScene is TilesetScene)
        {
            var World = ActiveScene.ECS.GetSystem<WorldGen>();
            ActiveScene.ECS.GetSystem<Tileset>().CameraPosition =
                new Vector2(0, 100 * World.TileSize - ActiveScene.Renderer.ScreenHeight / 3f);
        }
    });
    Window.Run();
}
else
    new GameClient().Run(Scene, new() { Title = $"Radiant — {Scene.GetType().Name}" });
