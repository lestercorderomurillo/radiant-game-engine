using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private const int MenuBarHeight = 44;
    private const int MenuBarPaddingX = 24;
    private const int MenuItemPaddingX = 30;
    private const int MenuItemHeight = 48;
    private const int MenuSeparatorHeight = 18;
    private static int MenuRowHeight(MenuItem item) => item.Type == MenuItemType.Separator ? MenuSeparatorHeight : MenuItemHeight;
    private const int MenuDropdownWidth = 400;
    private const float MenuBarFontSize = 28f;


    private List<MenuData> Menus = new();
    private string OpenMenuId;
    private Rectangle MenuBarBounds;
    private Rectangle OpenMenuDropdownBounds;

    private void InitializeMenuBar()
    {
        Menus.Clear();

        var about = new MenuData { Id = "about", Label = "Help" };
        about.Items.Add(new MenuItem
        {
            Id = "about_radiant",
            Label = "About Radiant",
            Type = MenuItemType.Action,
            ActionCallback = () =>
            {
                if (!Windows.ContainsKey("about_radiant"))
                {
                    CreateWindow("about_radiant", "About Radiant", 999, AutoPosition: false);
                    AddSectionLabel("about_radiant", "about_engine", "Radiant Engine");
                    AddLabel("about_radiant", "about_desc", "A lightweight 2D and 3D engine.");
                    AddLabel("about_radiant", "about_spacer1", " ");
                    AddLabel("about_radiant", "about_f2", "High-performance entity system.");
                    AddLabel("about_radiant", "about_f3", "GPU-accelerated rendering.");
                    AddLabel("about_radiant", "about_f1", "Built-in editor, systems and tools.");
                    AddLabel("about_radiant", "about_spacer2", " ");
                    AddLabel("about_radiant", "about_author", "Developed by Lester Cordero Murillo");
                    AddLabel("about_radiant", "about_build", ".NET 8.0 | Build 2026.02");
                }
                if (Windows.TryGetValue("about_radiant", out var aboutWindow))
                {
                    int windowHeight = ComputeWindowHeight(aboutWindow);
                    aboutWindow.Position = new Vector2(
                        (Renderer.VirtualWidth / UIScale - aboutWindow.Size.X) / 2,
                        (Renderer.VirtualHeight / UIScale - windowHeight) / 2);
                }
                ShowWindow("about_radiant");
            }
        });
        var view = new MenuData { Id = "view", Label = "View" };

        var ecs = new MenuData { Id = "ecs", Label = "ECS" };
        ecs.Items.Add(new MenuItem { Id = "entity_inspector", Label = "Open Entity Inspector", Type = MenuItemType.Action, ActionCallback = OpenEntityInspector });
        ecs.Items.Add(new MenuItem { Id = "component_inspector", Label = "Open Component Inspector", Type = MenuItemType.Action, ActionCallback = OpenComponentInspector });
        ecs.Items.Add(new MenuItem { Id = "system_inspector", Label = "Open System Inspector", Type = MenuItemType.Action, ActionCallback = OpenSystemsInspector });

        var file = new MenuData { Id = "file", Label = "File" };
        file.Items.Add(new MenuItem
        {
            Id = "close",
            Label = "Close",
            Type = MenuItemType.Action,
            ActionCallback = () => Renderer.Window.Exit()
        });

        Menus.Add(file);
        Menus.Add(ecs);
        Menus.Add(view);
        Menus.Add(about);

        RebuildViewMenu();
        ComputeMenuBarLayout();
    }

    private void RebuildViewMenu()
    {
        MenuData viewMenu = null;
        foreach (var menu in Menus)
        {
            if (menu.Id == "view") { viewMenu = menu; break; }
        }
        if (viewMenu == null) return;

        viewMenu.Items.Clear();

        viewMenu.Items.Add(new MenuItem
        {
            Id = "reorder_windows",
            Label = "Reorder Windows",
            Type = MenuItemType.Action,
            ActionCallback = () =>
            {
                LayoutDone = false;
                WindowsRestored?.Invoke();
            }
        });

        viewMenu.Items.Add(new MenuItem
        {
            Id = "show_all",
            Label = "Show All",
            Type = MenuItemType.Action,
            ActionCallback = () =>
            {
                foreach (var win in Windows.Values)
                {
                    if (win.Id == "about_radiant") continue;
                    win.Visible = true;
                }
                SyncViewToggleValues();
            }
        });

        viewMenu.Items.Add(new MenuItem
        {
            Id = "hide_all",
            Label = "Hide All",
            Type = MenuItemType.Action,
            ActionCallback = () =>
            {
                foreach (var win in Windows.Values)
                    win.Visible = false;
                SyncViewToggleValues();
            }
        });

        var ordered = new List<WindowData>(Windows.Values);
        ordered.Sort((a, b) =>
        {
            int category = a.Category.CompareTo(b.Category);
            if (category != 0) return category;
            int order = a.LayoutOrder.CompareTo(b.LayoutOrder);
            return order != 0 ? order : a.CreationIndex.CompareTo(b.CreationIndex);
        });

        bool sceneSection = false;
        foreach (var window in ordered)
        {
            if (window.Id == "about_radiant") continue;
            if (!sceneSection && window.Category == WindowCategory.Scene)
            {
                viewMenu.Items.Add(new MenuItem { Id = "scene_views_separator", Type = MenuItemType.Separator });
                sceneSection = true;
            }
            string windowId = window.Id;
            viewMenu.Items.Add(new MenuItem
            {
                Id = "show_" + windowId,
                Label = window.Title,
                Type = MenuItemType.Toggle,
                ToggleValue = window.Visible,
                ToggleCallback = (value) => SetWindowVisible(windowId, value)
            });
        }
        if (OpenMenuId == "view") OpenMenuDropdownBounds = GetMenuDropdownBounds(viewMenu);
    }

    private void SyncViewToggleValues()
    {
        MenuData viewMenu = null;
        foreach (var menu in Menus)
        {
            if (menu.Id == "view") { viewMenu = menu; break; }
        }
        if (viewMenu == null) return;

        for (int i = 0; i < viewMenu.Items.Count; i++)
        {
            var item = viewMenu.Items[i];
            if (item.Type != MenuItemType.Toggle) continue;

            string windowId = item.Id.Length > 5 ? item.Id[5..] : "";
            if (Windows.TryGetValue(windowId, out var window))
            {
                item.ToggleValue = window.Visible;
                viewMenu.Items[i] = item;
            }
        }
    }

    private void ComputeMenuBarLayout()
    {
        float virtualWidth = Renderer?.VirtualWidth ?? 3840;
        MenuBarBounds = new Rectangle(0, 0, (int)(virtualWidth / UIScale), MenuBarHeight);

        float currentX = MenuBarPaddingX;
        foreach (var menu in Menus)
        {
            var labelSize = Renderer?.MeasureString("Inter", MenuBarFontSize, menu.Label) ?? new Vector2(80, 22);
            int headerWidth = (int)labelSize.X + MenuItemPaddingX * 2;
            menu.HeaderBounds = new Rectangle((int)currentX, 0, headerWidth, MenuBarHeight);
            currentX += headerWidth;
        }
    }

    private bool UpdateMenuBar(Vector2 VirtualMouse, bool LeftPressed, KeyboardState Keyboard)
    {
        ComputeMenuBarLayout();

        if (OpenMenuId != null && Keyboard.IsKeyDown(Keys.Escape) && PrevKeyState.IsKeyUp(Keys.Escape))
        {
            CloseMenuDropdown();
            return true;
        }

        bool overBar = MenuBarBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);
        bool overDropdown = OpenMenuId != null && OpenMenuDropdownBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);

        if (overBar || overDropdown)
            MouseOverUI = true;

        if (overBar && OpenMenuId != null)
        {
            foreach (var menu in Menus)
            {
                if (menu.HeaderBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y) && menu.Id != OpenMenuId)
                {
                    OpenMenuDropdown(menu.Id);
                    return true;
                }
            }
        }

        if (LeftPressed)
        {
            if (overBar)
            {
                foreach (var menu in Menus)
                {
                    if (menu.HeaderBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y))
                    {
                        if (OpenMenuId == menu.Id)
                            CloseMenuDropdown();
                        else
                            OpenMenuDropdown(menu.Id);
                        return true;
                    }
                }
                return true;
            }

            if (overDropdown)
            {
                HandleMenuItemClick(VirtualMouse);
                return true;
            }

            if (OpenMenuId != null)
            {
                CloseMenuDropdown();
                return true;
            }
        }

        return overBar || overDropdown;
    }

    private void OpenMenuDropdown(string MenuId)
    {
        OpenMenuId = MenuId;

        if (MenuId == "view")
        {
            RebuildViewMenu();
            SyncViewToggleValues();
        }

        MenuData menu = null;
        foreach (var m in Menus)
        {
            if (m.Id == MenuId) { menu = m; break; }
        }
        if (menu == null) return;

        OpenMenuDropdownBounds = GetMenuDropdownBounds(menu);
    }

    private Rectangle GetMenuDropdownBounds(MenuData menu)
    {
        int height = 0;
        foreach (var item in menu.Items) height += MenuRowHeight(item);
        return new Rectangle(menu.HeaderBounds.X, MenuBarHeight, MenuDropdownWidth, height);
    }

    private void CloseMenuDropdown()
    {
        OpenMenuId = null;
    }

    private void HandleMenuItemClick(Vector2 Mouse)
    {
        MenuData menu = null;
        foreach (var m in Menus)
        {
            if (m.Id == OpenMenuId) { menu = m; break; }
        }
        if (menu == null) return;

        if (!OpenMenuDropdownBounds.Contains((int)Mouse.X, (int)Mouse.Y)) return;
        int localIndex = 0, rowY = OpenMenuDropdownBounds.Y;
        while (localIndex < menu.Items.Count && Mouse.Y >= rowY + MenuRowHeight(menu.Items[localIndex]))
        {
            rowY += MenuRowHeight(menu.Items[localIndex]);
            localIndex++;
        }
        if (localIndex >= menu.Items.Count) return;

        var item = menu.Items[localIndex];
        if (item.Type == MenuItemType.Separator) return;
        if (item.Type == MenuItemType.Toggle)
        {
            item.ToggleValue = !item.ToggleValue;
            menu.Items[localIndex] = item;
            item.ToggleCallback?.Invoke(item.ToggleValue);
        }
        else if (item.Type == MenuItemType.Action && item.ActionCallback != null)
        {
            item.ActionCallback.Invoke();
            CloseMenuDropdown();
        }
    }

    private void DrawMenuBar(Vector2 VirtualMouse)
    {
        var solid = Renderer.GetSolidTexture(Color.White);
        Color barBg = BlurResult != null ? GlassTint(TitleBarColor) : TitleBarColor;
        Renderer.DrawSprite(solid, MenuBarBounds, barBg);

        foreach (var menu in Menus)
        {
            bool isOpen = OpenMenuId == menu.Id;
            bool hovered = menu.HeaderBounds.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);

            if (isOpen || hovered)
            {
                Color highlightBg = BlurResult != null ? GlassTint(ButtonHover) : ButtonHover;
                Renderer.DrawSprite(solid, menu.HeaderBounds, highlightBg);
            }

            var labelSize = Renderer.MeasureString("Inter", MenuBarFontSize, menu.Label);
            var labelPos = new Vector2(
                menu.HeaderBounds.X + (menu.HeaderBounds.Width - labelSize.X) / 2,
                menu.HeaderBounds.Y + (menu.HeaderBounds.Height - labelSize.Y) / 2);
            Renderer.DrawString("Inter", MenuBarFontSize, menu.Label, labelPos, TextColor);
        }
    }

    private const float MenuBarSmallFontSize = 22f;

    private (string Text, float FontSize) FitMenuItemText(string label, float maxWidth)
    {
        var size = Renderer.MeasureString("Inter", MenuBarFontSize, label);
        if (size.X <= maxWidth) return (label, MenuBarFontSize);

        size = Renderer.MeasureString("Inter", MenuBarSmallFontSize, label);
        if (size.X <= maxWidth) return (label, MenuBarSmallFontSize);

        var ellipsis = "...";
        for (int length = label.Length - 1; length > 0; length--)
        {
            var truncated = label[..length] + ellipsis;
            size = Renderer.MeasureString("Inter", MenuBarSmallFontSize, truncated);
            if (size.X <= maxWidth) return (truncated, MenuBarSmallFontSize);
        }
        return (ellipsis, MenuBarSmallFontSize);
    }

    private void DrawMenuDropdown(Vector2 VirtualMouse)
    {
        if (OpenMenuId == null) return;

        MenuData menu = null;
        foreach (var m in Menus)
        {
            if (m.Id == OpenMenuId) { menu = m; break; }
        }
        if (menu == null) return;

        bool glass = BlurResult != null;
        Color dropdownBg = glass ? GlassTint(WindowBg) : WindowBg;
        Renderer.DrawRoundedRect(OpenMenuDropdownBounds, dropdownBg, CornerRadius, RoundedCorners.Bottom);

        var solid = Renderer.GetSolidTexture(Color.White);
        int rowY = OpenMenuDropdownBounds.Y;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];
            var itemRect = new Rectangle(OpenMenuDropdownBounds.X, rowY, OpenMenuDropdownBounds.Width, MenuRowHeight(item));
            rowY += itemRect.Height;
            if (item.Type == MenuItemType.Separator)
            {
                Renderer.DrawSprite(solid, new Rectangle(itemRect.X + Padding, itemRect.Center.Y, itemRect.Width - Padding * 2, 1), LabelDim * 0.5f);
                continue;
            }
            bool hovered = itemRect.Contains((int)VirtualMouse.X, (int)VirtualMouse.Y);

            if (hovered)
            {
                RoundedCorners corners = menu.Items.Count == 1 ? RoundedCorners.Bottom :
                    i == menu.Items.Count - 1 ? RoundedCorners.Bottom : RoundedCorners.None;
                Renderer.DrawRoundedRect(itemRect, glass ? GlassTint(ButtonHover) : ButtonHover, CornerRadius, corners);
            }

            if (item.Type == MenuItemType.Toggle)
            {
                int boxY = itemRect.Y + (itemRect.Height - ToggleBoxSize) / 2;
                var boxRect = new Rectangle(itemRect.X + Padding, boxY, ToggleBoxSize, ToggleBoxSize);
                Renderer.DrawRoundedRect(boxRect, item.ToggleValue ? ToggleOn : ToggleOff, 4);

                if (item.ToggleValue)
                {
                    int checkSize = ToggleBoxSize - 4;
                    var checkTex = Renderer.GetCheckmarkTexture(checkSize * 4);
                    var checkRect = new Rectangle(boxRect.X + 2, boxRect.Y + 2, checkSize, checkSize);
                    Renderer.DrawSprite(checkTex, checkRect, CloseText);
                }

                float textStartX = itemRect.X + Padding + ToggleBoxSize + 8;
                float maxTextWidth = itemRect.Right - textStartX - Padding;
                var (fitText, fitSize) = FitMenuItemText(item.Label, maxTextWidth);
                float fitLineHeight = Renderer.MeasureString("Inter", fitSize, fitText).Y;
                var textPos = new Vector2(textStartX, itemRect.Y + (itemRect.Height - fitLineHeight) / 2);
                Renderer.DrawString("Inter", fitSize, fitText, textPos, TextColor);
            }
            else
            {
                float textStartX = itemRect.X + Padding;
                float maxTextWidth = itemRect.Right - textStartX - Padding;
                var (fitText, fitSize) = FitMenuItemText(item.Label, maxTextWidth);
                float fitLineHeight = Renderer.MeasureString("Inter", fitSize, fitText).Y;
                var textPos = new Vector2(textStartX, itemRect.Y + (itemRect.Height - fitLineHeight) / 2);
                Renderer.DrawString("Inter", fitSize, fitText, textPos, TextColor);
            }
        }
    }

    private readonly List<int> EntityIdCache = new();
    private bool EntityListDirty;
    private readonly List<int> FilteredEntityIds = new();
    private long ListedEntityRevision = -1;

    private void UpdateEntityInspector()
    {
        if (!IsWindowVisibleInternal("entity_inspector")) return;

        if (Scene.ECS.EntityRevision != ListedEntityRevision || EntityListDirty)
        {
            EntityListDirty = false;
            RefreshEntityList();
        }
    }

    private void RefreshEntityList()
    {
        ListedEntityRevision = Scene.ECS.EntityRevision;
        var selectedIds = GetSelectedEntityIds();
        EntityIdCache.Clear(); Scene.ECS.GetAllEntityIds(EntityIdCache); EntityIdCache.Sort();
        FilteredEntityIds.Clear();
        string filter = GetTextInputValue("entity_inspector", "find_input");
        var items = new List<string>();
        foreach (int id in EntityIdCache)
        {
            var types = Scene.ECS.GetComponentTypes(id);
            string names = string.Join(", ", Array.ConvertAll(types, type => type.Name));
            if (filter.Length > 0 && !id.ToString().Contains(filter) && !names.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            FilteredEntityIds.Add(id);
            char prefix = Scene.ECS.IsDisabled(id) ? '\x07' : '\x06';
            items.Add($"{prefix}{id}  |  {names}");
        }
        SetListBoxItems("entity_inspector", "results_list", items.ToArray());
        var selection = GetListBoxSelected("entity_inspector", "results_list");
        selection?.Clear();
        if (selectedIds != null)
            foreach (int id in selectedIds)
            {
                int index = FilteredEntityIds.IndexOf(id);
                if (index >= 0) selection?.Add(index);
            }
    }

    private void ToggleEntityVisibility(int listIndex)
    {
        if (listIndex < 0 || listIndex >= FilteredEntityIds.Count) return;
        int entity = FilteredEntityIds[listIndex];
        if (!Scene.ECS.IsAlive(entity)) return;
        if (Scene.ECS.IsDisabled(entity)) Scene.ECS.EnableEntity(entity); else Scene.ECS.DisableEntity(entity);
        EntityListDirty = true;
    }

    private void DeleteSelectedEntities()
    {
        var selected = GetSelectedEntityIds();
        if (selected == null) return;
        foreach (int entity in selected) Scene.ECS.ScheduleDestroy(entity);
        EntityListDirty = true;
    }

    private List<int> GetSelectedEntityIds()
    {
        var selected = GetListBoxSelected("entity_inspector", "results_list");
        if (selected == null || selected.Count == 0) return null;
        var ids = new List<int>();
        foreach (int index in selected)
            if (index >= 0 && index < FilteredEntityIds.Count && Scene.ECS.IsAlive(FilteredEntityIds[index])) ids.Add(FilteredEntityIds[index]);
        return ids.Count > 0 ? ids : null;
    }

    private void OpenEntityInspector()
    {
        if (!Windows.ContainsKey("entity_inspector"))
        {
            CreateWindow("entity_inspector", "Entity Inspector", 50);
            if (Windows.TryGetValue("entity_inspector", out var entityInspectorWindow))
                entityInspectorWindow.Resizable = true;
            AddSectionLabel("entity_inspector", "find_section", "Find Entity");
            AddTextInput("entity_inspector", "find_input", "Search ID or component...", (value) => RefreshEntityList(), 1f);
            AddListBox("entity_inspector", "results_list", 500);
            SetListBoxHeader("entity_inspector", "results_list", "ID  |  Components");
            SetListBoxToggleCallback("entity_inspector", "results_list", ToggleEntityVisibility);
            AddButton("entity_inspector", "delete_btn", "trash", () => DeleteSelectedEntities());
            RefreshEntityList();
        }
        ShowWindow("entity_inspector");
    }

    private void OpenComponentInspector()
    {
        if (!Windows.ContainsKey("component_inspector"))
        {
            CreateWindow("component_inspector", "Component Inspector", 52);
            if (Windows.TryGetValue("component_inspector", out var componentWindow))
                componentWindow.Resizable = true;
            AddSectionLabel("component_inspector", "components_section", "Find Component");
            AddTextInput("component_inspector", "components_filter", "Search by Name...", (value) => RefreshComponentList(), 1f);
            AddListBox("component_inspector", "components_list", 500);
            RefreshComponentList();
        }
        ShowWindow("component_inspector");
    }

    private void RefreshComponentList()
    {
        var registeredTypes = Scene.ECS.GetRegisteredComponentTypes();
        string filter = GetTextInputValue("component_inspector", "components_filter");

        var sorted = new List<Type>(registeredTypes);
        sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        List<string> items = new();
        foreach (var type in sorted)
        {
            string name = type.Name;
            var descAttr = (ComponentDescriptionAttribute)Attribute.GetCustomAttribute(type, typeof(ComponentDescriptionAttribute));
            string description = descAttr?.Description;

            if (filter.Length > 0)
            {
                bool matches = name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (description != null && description.Contains(filter, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
            }

            items.Add($"\x03{name}");
            if (description != null)
                items.Add(description);
        }
        SetListBoxItems("component_inspector", "components_list", items.ToArray());
    }

    private void OpenSystemsInspector()
    {
        if (!Windows.ContainsKey("system_inspector"))
        {
            CreateWindow("system_inspector", "System Inspector", 51);

            if (Windows.TryGetValue("system_inspector", out var systemWindow))
                systemWindow.Resizable = true;

            AddSectionLabel("system_inspector", "systems_section", "Find System");
            AddTextInput("system_inspector", "systems_filter", "Search by Name...", (value) => RefreshSystemList(), 1f);
            AddListBox("system_inspector", "systems_list", 500);
            SetListBoxToggleCallback("system_inspector", "systems_list", ToggleSystemAtIndex);

            RefreshSystemList();
        }
        ShowWindow("system_inspector");
    }

    private void UpdateSystemsInspector()
    {
        if (!IsWindowVisibleInternal("system_inspector")) return;

        if (!Windows.TryGetValue("system_inspector", out var window)) return;
        if (!window.WidgetIndex.TryGetValue("systems_list", out int widgetIdx)) return;

        var widget = window.Widgets[widgetIdx];
        var items = widget.ListBoxItems;

        if (items == null || items.Length != SystemListMapping.Count) { RefreshSystemList(preserveSelection: true); return; }

        bool changed = false;
        for (int i = 0; i < SystemListMapping.Count; i++)
        {
            var system = SystemListMapping[i];
            if (system == null) continue;
            if (items[i].Length == 0) { changed = true; break; }
            bool dotEnabled = items[i][0] == '\x01' || items[i][0] == '\x04';
            if (dotEnabled != system.Enabled) { changed = true; break; }
        }

        if (changed) RefreshSystemList(preserveSelection: true);
    }

    private static bool IsCoreSystem(core.System system) =>
        Attribute.IsDefined(system.GetType(), typeof(CoreSystemAttribute));

    private string BuildSystemGroupKey(core.System system) => Scene.ECS.GetGroupName(system);

    private string BuildSystemTags(core.System system)
    {
        var parts = new List<string>();
        var groupName = Scene.ECS.GetGroupName(system);
        if (groupName != null) parts.Add(groupName);
        var tagAttr = (SystemTagAttribute)Attribute.GetCustomAttribute(system.GetType(), typeof(SystemTagAttribute));

        if (tagAttr != null) parts.AddRange(tagAttr.Tags);
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private List<core.System> GetFilteredSystems()
    {
        var allSystems = Scene.ECS.GetAllSystems();
        string filter = GetTextInputValue("system_inspector", "systems_filter");
        List<core.System> filtered = new();
        foreach (var system in allSystems)
        {
            if (IsCoreSystem(system)) continue;
            if (filter.Length > 0)
            {
                string name = system.GetType().Name;
                string tags = BuildSystemTags(system);
                bool matches = name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (tags != null && tags.Contains(filter, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
            }
            filtered.Add(system);
        }
        return filtered;
    }

    private readonly List<core.System> SystemListMapping = new();

    private void RefreshSystemList(bool preserveSelection = false)
    {
        HashSet<int> savedSelection = null;
        if (preserveSelection)
        {
            var current = GetListBoxSelected("system_inspector", "systems_list");
            if (current != null && current.Count > 0)
                savedSelection = new HashSet<int>(current);
        }

        var filtered = GetFilteredSystems();

        var groups = new List<(string Name, List<core.System> Systems)>();
        var groupMap = new Dictionary<string, int>();
        var ungrouped = new List<core.System>();

        foreach (var system in filtered)
        {
            string groupKey = BuildSystemGroupKey(system);
            if (groupKey != null)
            {
                if (!groupMap.TryGetValue(groupKey, out int gi))
                {
                    gi = groups.Count;
                    groupMap[groupKey] = gi;
                    groups.Add((groupKey, new List<core.System>()));
                }
                groups[gi].Systems.Add(system);
            }
            else
            {
                ungrouped.Add(system);
            }
        }

        List<string> items = new();
        SystemListMapping.Clear();

        foreach (var (name, systems) in groups)
        {
            items.Add($"\x03{name}");
            SystemListMapping.Add(null);
            foreach (var system in systems)
            {
                string marker = system.Enabled ? "\x04" : "\x05";
                items.Add($"{marker}{system.GetType().Name}");
                SystemListMapping.Add(system);
            }
        }

        if (ungrouped.Count > 0)
        {
            foreach (var system in ungrouped)
            {
                string marker = system.Enabled ? "\x01" : "\x02";
                items.Add($"{marker}{system.GetType().Name}");
                SystemListMapping.Add(system);
            }
        }

        SetListBoxItems("system_inspector", "systems_list", items.ToArray());

        if (savedSelection != null && savedSelection.Count > 0)
        {
            if (Windows.TryGetValue("system_inspector", out var window) &&
                window.WidgetIndex.TryGetValue("systems_list", out int widgetIdx))
            {
                var widget = window.Widgets[widgetIdx];
                widget.ListBoxSelected = savedSelection;
                window.Widgets[widgetIdx] = widget;
            }
        }
    }

    private void ToggleSystemAtIndex(int index)
    {
        if (index < 0 || index >= SystemListMapping.Count) return;
        var system = SystemListMapping[index];
        if (system == null) return;

        var group = Scene.ECS.GetSystemGroup(system);

        if (group != null)
        {
            int groupIndex = group.IndexOf(system);
            if (groupIndex >= 0)
            {
                if (system.Enabled)
                    group.DisableActive();
                else
                    group.SetActive(groupIndex);
            }
        }
        else
        {
            system.Enabled = !system.Enabled;
        }

        RefreshSystemList(preserveSelection: true);
    }

    private static readonly Color GizmoSelectionColor = new(0, 255, 100, 200);

    public override void Render()
    {
        if (!GlobalVisible) return;
        if (!IsWindowVisible("entity_inspector")) return;

        var gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        if (gizmos == null) return;

        var selectedIds = GetSelectedEntityIds();
        if (selectedIds == null) return;

        foreach (int entityId in selectedIds)
        {
            if (!Scene.ECS.IsAlive(entityId)) continue;
            
            if (!Scene.ECS.HasComponent<Transform2D>(entityId)) continue;
            ref var transform = ref Scene.ECS.GetComponent<Transform2D>(entityId);
            var position = new Vector2(transform.Position.X, transform.Position.Y);

            if (Scene.ECS.HasComponent<Circle2D>(entityId))
            {
                ref var circle = ref Scene.ECS.GetComponent<Circle2D>(entityId);
                gizmos.AddGizmoCircle(position, circle.Radius + 4f, GizmoSelectionColor);
            }
            else if (Scene.ECS.HasComponent<Rectangle2D>(entityId))
            {
                ref var rectangle = ref Scene.ECS.GetComponent<Rectangle2D>(entityId);

                var rect = new Rectangle(
                    (int)(position.X - rectangle.Size.X / 2 - 4),
                    (int)(position.Y - rectangle.Size.Y / 2 - 4),
                    (int)(rectangle.Size.X + 8),
                    (int)(rectangle.Size.Y + 8));

                gizmos.AddGizmoRect(rect, GizmoSelectionColor);
            }
            else if (Scene.ECS.HasComponent<Triangle2D>(entityId))
            {
                ref var triangle = ref Scene.ECS.GetComponent<Triangle2D>(entityId);
                float radius = Math.Max(triangle.Size.X, triangle.Size.Y) / 2f + 4f;
                gizmos.AddGizmoCircle(position, radius, GizmoSelectionColor);
            }
            else
            {
                gizmos.AddGizmoCircle(position, 12f, GizmoSelectionColor);
            }
        }
    }
}
