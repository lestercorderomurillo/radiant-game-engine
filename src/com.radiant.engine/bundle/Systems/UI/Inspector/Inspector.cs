using System;
using System.Collections.Generic;
using com.radiant.engine.core;
using com.radiant.engine.input;

namespace com.radiant.engine.bundle;

/// <summary>Displays in-game diagnostic windows and controls for inspecting and tuning a scene.</summary>
/// <remarks>Add the system to the scene and use static widget helpers to register panels once, then update values during gameplay.
/// F1 toggles visibility. Check IsMouseOverUI and IsEditingText before handling overlapping gameplay input.
/// Use GizmosRenderer for diagnostic shapes drawn over the scene.</remarks>
/// <seealso cref="GizmosRenderer"/>
[CoreSystem]
[RunAfter(typeof(Geometry))]
[RunBefore(typeof(GizmosRenderer))]
public partial class Inspector : core.System
{
    public override RenderLayer RenderLayer => RenderLayer.UI;

    private static Inspector Instance;

    /// <summary>Initializes inspector window state and built-in appearance themes.</summary>
    public Inspector() => Instance = this;

    private const float FontSize = 30f;
    private float LineHeight;
    private Dictionary<string, WindowData> Windows = new();
    private List<WindowData> RenderOrder = new();
    private int NextZOrder;
    private int NextCreationIndex;
    private bool LayoutDone;

    private MouseState PrevMouse;
    private KeyboardState PrevKeyState;
    private bool Dragging;
    private string DragWindowId;
    private Vector2 DragOffset;
    private bool DraggingSlider;
    private string SliderWindowId;
    private string SliderWidgetId;
    private bool MouseOverUI;
    private bool GlobalVisible = false;
    private bool Resizing;
    private string ResizeWindowId;
    private Vector2 ResizeStartMouse;
    private float ResizeStartWidth;
    private float ResizeStartHeight;
    private bool DraggingListBox;
    private string ListBoxDragWindowId;
    private string ListBoxDragWidgetId;
    private int ListBoxDragStartIndex;
    private HashSet<int> ListBoxDragBaseSelection;
    private bool DraggingScrollbar;
    private string ScrollbarDragWindowId;
    private string ScrollbarDragWidgetId;

    private string OpenDropdownWindowId;
    private string OpenDropdownWidgetId;
    private Rectangle OpenDropdownPopupBounds;
    private int DropdownScrollOffset;
    private int DropdownTotalOptions;

    private string FocusedTextInputWindowId;
    private string FocusedTextInputWidgetId;
    private float CursorBlinkTimer;
    private const float CursorBlinkRate = 0.53f;
    private const int InlineGap = 10;

    /// <summary>Raised when the user picks View → Reorder Windows, after windows return to their default places.</summary>
    public static event Action WindowsRestored;

    private const int DefaultWindowWidth = 600;
    private const int TitleBarHeight = 64;
    private const int WidgetHeight = 60;
    private const int WidgetSpacing = 18;
    private const int LabelSpacing = 6;
    private const int Padding = 20;
    private const int CloseButtonSize = 28;
    private const int AutoLayoutGap = 32;
    private const int SliderTrackHeight = 8;
    private const int SliderHandleSize = 22;
    private const int ToggleBoxSize = 34;
    private const int CornerRadius = 10;
    private const int MaxVisibleDropdownItems = 4;
    private const int ResizeHandleSize = 32;
    private const int MinWindowWidth = 340;
    private const int MinWindowHeight = 180;

    private static float UIScaleBacking = 1.0f;

    private static float UIScale
    {
        get => UIScaleBacking;
        set
        {
            UIScaleBacking = value;
            if (Instance?.Renderer != null)
            {
                Instance.Renderer.FontRenderScale = MathF.Max(1f, 2f * value * Instance.Renderer.ScreenHeight / Instance.Renderer.VirtualHeight);
                Instance.LineHeight = Instance.Renderer.GetLineHeight("Inter", FontSize);
            }
        }
    }

    /// <summary>Creates a window you then fill with widgets. Lower <paramref name="LayoutOrder"/> values are placed first when windows auto-arrange.</summary>
    public static void CreateWindow(string Id, string Title, int LayoutOrder = 100, bool AutoPosition = true, bool Visible = true, WindowCategory Category = WindowCategory.System)
        => Instance?.CreateWindowInternal(Id, Title, LayoutOrder, AutoPosition, Visible, Category);

    /// <summary>Creates a window for your game's own panels, listed separately from engine tools in the View menu.</summary>
    public static void CreateSceneWindow(string Id, string Title, int LayoutOrder = 100, bool AutoPosition = true, bool Visible = true)
        => CreateWindow(Id, Title, LayoutOrder, AutoPosition, Visible, WindowCategory.Scene);

    /// <summary>Removes a window and everything in it.</summary>
    public static void DestroyWindow(string Id)
        => Instance?.DestroyWindowInternal(Id);

    /// <summary>Shows a window.</summary>
    public static void ShowWindow(string Id) => Instance?.SetWindowVisible(Id, true);

    /// <summary>Hides a window without destroying it.</summary>
    public static void HideWindow(string Id) => Instance?.SetWindowVisible(Id, false);

    /// <summary>Shows a hidden window or hides a visible one.</summary>
    public static void ToggleWindow(string Id) => Instance?.ToggleWindowInternal(Id);

    /// <summary>True if the window exists and is showing.</summary>
    public static bool IsWindowVisible(string Id) => Instance?.IsWindowVisibleInternal(Id) ?? false;

    /// <summary>Adds a line of text. Change it every frame with <c>SetLabel</c> to show live values.</summary>
    public static void AddLabel(string WindowId, string WidgetId, string Text)
        => Instance?.AddWidgetInternal(WindowId, WidgetId, WidgetType.Label, Text);

    /// <summary>Adds a bold heading with a divider to group the widgets below it.</summary>
    public static void AddSectionLabel(string WindowId, string WidgetId, string Text)
        => Instance?.AddSectionLabelInternal(WindowId, WidgetId, Text);

    /// <summary>Adds a button that runs your callback when clicked.</summary>
    public static void AddButton(string WindowId, string WidgetId, string Text, Action Callback)
        => Instance?.AddButtonInternal(WindowId, WidgetId, Text, Callback);

    /// <summary>Adds an on/off switch. Your callback receives the new value.</summary>
    public static void AddToggle(string WindowId, string WidgetId, string Text, bool Initial, Action<bool> Callback)
        => Instance?.AddToggleInternal(WindowId, WidgetId, Text, Initial, Callback);

    /// <summary>Adds a slider between <paramref name="Min"/> and <paramref name="Max"/>. Your callback receives each new value while dragging.</summary>
    public static void AddSlider(string WindowId, string WidgetId, string Text, float Min, float Max, float Initial, Action<float> Callback)
        => Instance?.AddSliderInternal(WindowId, WidgetId, Text, Min, Max, Initial, Callback);

    /// <summary>Adds a dropdown. Your callback receives the index of the chosen option.</summary>
    /// <example>
    /// The following example shows how to switch quality presets:
    /// <code>
    /// Inspector.AddDropdown("graphics", "quality", "Quality", ["Low", "Medium", "High"], 1, index => ApplyQuality(index));
    /// </code>
    /// </example>
    public static void AddDropdown(string WindowId, string WidgetId, string Text, string[] Options, int Initial, Action<int> Callback)
        => Instance?.AddDropdownInternal(WindowId, WidgetId, Text, Options, Initial, Callback);

    /// <summary>Adds a text field. Give it an <paramref name="InlineRatio"/> to share a row with the widget that follows.</summary>
    /// <example>
    /// The following example shows how to build a search bar from a field and a button on one row:
    /// <code>
    /// Inspector.AddTextInput("entities", "query", "Search…", text => Query = text, 0.8f);
    /// Inspector.AddButton("entities", "go", "", Search, 0.2f); // Empty text shows a search icon
    /// </code>
    /// </example>
    public static void AddTextInput(string WindowId, string WidgetId, string Placeholder, Action<string> Callback, float InlineRatio = 0f)
        => Instance?.AddTextInputInternal(WindowId, WidgetId, Placeholder, Callback, InlineRatio);

    /// <summary>Replaces the text in a text field.</summary>
    public static void SetTextInputValue(string WindowId, string WidgetId, string Value)
        => Instance?.SetTextInputValueInternal(WindowId, WidgetId, Value);

    /// <summary>Returns the text currently in a text field.</summary>
    public static string GetTextInputValue(string WindowId, string WidgetId)
        => Instance?.GetTextInputValueInternal(WindowId, WidgetId) ?? "";

    /// <summary>Adds a scrollable list where users can select rows. <paramref name="Height"/> is in pixels.</summary>
    /// <example>
    /// The following example shows a two-column list of entities:
    /// <code>
    /// Inspector.AddListBox("world", "entities", 300);
    /// Inspector.SetListBoxHeader("world", "entities", "ID  |  Name");
    /// Inspector.SetListBoxItems("world", "entities", ["1  |  Player", "2  |  Lamp"]);
    ///
    /// var selected = Inspector.GetListBoxSelected("world", "entities");
    /// </code>
    /// </example>
    public static void AddListBox(string WindowId, string WidgetId, int Height, string[] Items = null)
        => Instance?.AddListBoxInternal(WindowId, WidgetId, Height, Items);

    /// <summary>Replaces the rows of a list. Separate columns with <c>"  |  "</c>.</summary>
    public static void SetListBoxItems(string WindowId, string WidgetId, string[] Items)
        => Instance?.SetListBoxItemsInternal(WindowId, WidgetId, Items);

    /// <summary>Returns the indices of the selected rows.</summary>
    public static HashSet<int> GetListBoxSelected(string WindowId, string WidgetId)
        => Instance?.GetListBoxSelectedInternal(WindowId, WidgetId);

    /// <summary>Deselects every row.</summary>
    public static void ClearListBoxSelection(string WindowId, string WidgetId)
        => Instance?.ClearListBoxSelectionInternal(WindowId, WidgetId);

    /// <summary>Adds a fixed header row; separate columns with <c>"  |  "</c>.</summary>
    public static void SetListBoxHeader(string WindowId, string WidgetId, string Header)
        => Instance?.SetListBoxHeaderInternal(WindowId, WidgetId, Header);

    /// <summary>Runs your callback with the row index when a row's checkbox is clicked.</summary>
    public static void SetListBoxToggleCallback(string WindowId, string WidgetId, Action<int> Callback)
        => Instance?.SetListBoxToggleCallbackInternal(WindowId, WidgetId, Callback);

    /// <summary>Adds a button that shares a row with neighboring widgets, taking <paramref name="InlineRatio"/> of the width. Empty text shows a search icon.</summary>
    public static void AddButton(string WindowId, string WidgetId, string Text, Action Callback, float InlineRatio)
        => Instance?.AddButtonInternal(WindowId, WidgetId, Text, Callback, InlineRatio);

    /// <summary>Removes one widget from a window.</summary>
    public static void RemoveWidget(string WindowId, string WidgetId)
        => Instance?.RemoveWidgetInternal(WindowId, WidgetId);

    /// <summary>Greys out a widget and ignores clicks while disabled.</summary>
    public static void SetWidgetEnabled(string WindowId, string WidgetId, bool Enabled)
        => Instance?.SetWidgetEnabledInternal(WindowId, WidgetId, Enabled);

    /// <summary>Changes a label's text. Cheap enough to call every frame.</summary>
    public static void SetLabel(string WindowId, string WidgetId, string Text)
        => Instance?.SetLabelInternal(WindowId, WidgetId, Text);

    /// <summary>Moves a slider to a value without calling its callback.</summary>
    public static void SetSliderValue(string WindowId, string WidgetId, float Value)
        => Instance?.SetSliderValueInternal(WindowId, WidgetId, Value);

    /// <summary>Sets a toggle without calling its callback.</summary>
    public static void SetToggleValue(string WindowId, string WidgetId, bool Value)
        => Instance?.SetToggleValueInternal(WindowId, WidgetId, Value);

    /// <summary>Selects a dropdown option without calling its callback.</summary>
    public static void SetDropdownValue(string WindowId, string WidgetId, int Index)
        => Instance?.SetDropdownValueInternal(WindowId, WidgetId, Index);

    /// <summary>Replaces a dropdown's options, keeping the selection when it is still valid.</summary>
    public static void SetDropdownOptions(string WindowId, string WidgetId, string[] Options)
        => Instance?.SetDropdownOptionsInternal(WindowId, WidgetId, Options);

    /// <summary>True when the mouse is over an inspector window or menu. Check it before reacting to world clicks.</summary>
    /// <example>
    /// The following example shows how to paint lights without painting through the UI:
    /// <code>
    /// if (Controls.IsDown(MouseButton.Left) &amp;&amp; !Inspector.IsMouseOverUI())
    ///     LightFactory.CreateLight(ECS, Renderer.ScreenToWorld(Controls.Mouse.Position), 12f, Color.White, Color.Gold);
    /// </code>
    /// </example>
    public static bool IsMouseOverUI() => Instance?.GlobalVisible == true && Instance.MouseOverUI;

    /// <summary>Creates or retrieves an inspector window with the requested identity and ordering.</summary>
    private void CreateWindowInternal(string Id, string Title, int LayoutOrder, bool AutoPosition, bool Visible, WindowCategory Category)
    {
        if (Windows.ContainsKey(Id)) return;

        var window = new WindowData
        {
            Id = Id,
            Title = Title,
            Category = Category,
            Position = Vector2.Zero,
            Size = new Vector2(DefaultWindowWidth, 0),
            Visible = Visible,
            ZOrder = NextZOrder++,
            CreationIndex = NextCreationIndex++,
            LayoutOrder = LayoutOrder,
            AutoPosition = AutoPosition,
            Resizable = true
        };
        Windows[Id] = window;
        RenderOrder.Add(window);
        SortRenderOrder();
        RebuildViewMenu();
        PlaceNewWindow(window);
        if (CurrentWorkspaceMode == WorkspaceMode.Panels) ActivateDock(window);
    }

    /// <summary>Removes an inspector window and clears its input focus.</summary>
    private void DestroyWindowInternal(string Id)
    {
        if (!Windows.TryGetValue(Id, out var window)) return;

        if (FocusedTextInputWindowId == Id) ClearTextInputFocus();
        Windows.Remove(Id);
        FloatingLayout.Remove(Id);
        DockScroll.Remove(Id);
        DockTabOrder.Remove(Id);
        DockAssignments.Remove(Id);
        if (DragTabId == Id) CancelTabDrag();
        RenderOrder.Remove(window);
        RebuildViewMenu();
    }

    /// <summary>Changes a window's visibility and updates its input state.</summary>
    private void SetWindowVisible(string Id, bool Visible)
    {
        if (Windows.TryGetValue(Id, out var window))
        {
            window.Visible = Visible;
            if (!Visible && FocusedTextInputWindowId == Id) ClearTextInputFocus();

            if (Visible)
            {
                BringToFront(window);
                if (CurrentWorkspaceMode == WorkspaceMode.Windows) PlaceNewWindow(window);
                else ActivateDock(window);
            }
        }
    }

    /// <summary>Toggles visibility of the named inspector window.</summary>
    private void ToggleWindowInternal(string Id)
    {
        if (Windows.TryGetValue(Id, out var window)) SetWindowVisible(Id, !window.Visible);
    }

    /// <summary>Reports whether the named inspector window is currently visible.</summary>
    private bool IsWindowVisibleInternal(string Id)
        => Windows.TryGetValue(Id, out var window) && window.Visible;

    /// <summary>Registers a widget in its window and updates layout bookkeeping.</summary>
    private void AddWidgetInternal(string WindowId, string WidgetId, WidgetType Type, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget { Id = WidgetId, Type = Type, Text = Text, Visible = true });
    }

    /// <summary>Adds a section heading to an inspector window.</summary>
    private void AddSectionLabelInternal(string WindowId, string WidgetId, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget { Id = WidgetId, Type = WidgetType.Label, Text = Text, Visible = true, Section = true });
    }

    /// <summary>Adds a button and its click callback to an inspector window.</summary>
    private void AddButtonInternal(string WindowId, string WidgetId, string Text, Action Callback, float InlineRatio = 0f)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Button, Text = Text, Visible = true,
            ButtonCallback = Callback, InlineRatio = InlineRatio
        });
    }

    /// <summary>Adds an editable text field and its change callback.</summary>
    private void AddTextInputInternal(string WindowId, string WidgetId, string Placeholder, Action<string> Callback, float InlineRatio)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.TextInput, Visible = true,
            TextInputValue = "", TextInputPlaceholder = Placeholder,
            TextInputCallback = Callback, TextInputCursor = 0, InlineRatio = InlineRatio
        });
    }

    /// <summary>Replaces a text field's value while preserving valid cursor state.</summary>
    private void SetTextInputValueInternal(string WindowId, string WidgetId, string Value)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.TextInputValue = Value ?? "";
        widget.TextInputCursor = widget.TextInputValue.Length;
        window.Widgets[index] = widget;
    }

    /// <summary>Returns the current value of a named text field.</summary>
    private string GetTextInputValueInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return "";
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return "";

        return window.Widgets[index].TextInputValue ?? "";
    }

    /// <summary>Adds a selectable list and its selection callback.</summary>
    private void AddListBoxInternal(string WindowId, string WidgetId, int Height, string[] Items)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.ListBox, Visible = true,
            ListBoxHeight = Height, ListBoxItems = Items ?? Array.Empty<string>(),
            ListBoxSelected = new HashSet<int>()
        });
    }

    /// <summary>Replaces list items and reconciles selection with the new contents.</summary>
    private void SetListBoxItemsInternal(string WindowId, string WidgetId, string[] Items)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.ListBoxItems = Items ?? Array.Empty<string>();
        widget.ListBoxSelected ??= new HashSet<int>();
        widget.ListBoxSelected.Clear();
        if (widget.ListBoxScroll >= widget.ListBoxItems.Length) widget.ListBoxScroll = Math.Max(0, widget.ListBoxItems.Length - 1);
        window.Widgets[index] = widget;
    }

    /// <summary>Returns the selected indices for a named list box.</summary>
    private HashSet<int> GetListBoxSelectedInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return null;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return null;

        return window.Widgets[index].ListBoxSelected;
    }

    /// <summary>Clears every selected row in a list box.</summary>
    private void ClearListBoxSelectionInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        window.Widgets[index].ListBoxSelected?.Clear();
    }

    /// <summary>Changes the header displayed above a list box.</summary>
    private void SetListBoxHeaderInternal(string WindowId, string WidgetId, string Header)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.ListBoxHeader = Header;
        window.Widgets[index] = widget;
    }

    /// <summary>Sets the callback used when a list row's toggle changes.</summary>
    private void SetListBoxToggleCallbackInternal(string WindowId, string WidgetId, Action<int> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.ListBoxToggleCallback = Callback;
        window.Widgets[index] = widget;
    }

    /// <summary>Adds a boolean control and its change callback.</summary>
    private void AddToggleInternal(string WindowId, string WidgetId, string Text, bool Initial, Action<bool> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Toggle, Text = Text, Visible = true,
            ToggleValue = Initial, ToggleCallback = Callback
        });
    }

    /// <summary>Adds a bounded numeric slider and its change callback.</summary>
    private void AddSliderInternal(string WindowId, string WidgetId, string Text, float Min, float Max, float Initial, Action<float> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Slider, Text = Text, Visible = true,
            SliderValue = Initial, SliderMin = Min, SliderMax = Max, SliderCallback = Callback
        });
    }

    /// <summary>Adds a dropdown with selectable labels and a selection callback.</summary>
    private void AddDropdownInternal(string WindowId, string WidgetId, string Text, string[] Options, int Initial, Action<int> Callback)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (window.WidgetIndex.ContainsKey(WidgetId)) return;

        window.WidgetIndex[WidgetId] = window.Widgets.Count;
        window.Widgets.Add(new Widget
        {
            Id = WidgetId, Type = WidgetType.Dropdown, Text = Text, Visible = true,
            DropdownOptions = Options, DropdownSelected = Initial, DropdownCallback = Callback
        });
    }

    /// <summary>Removes a widget from its window and updates lookup state.</summary>
    private void RemoveWidgetInternal(string WindowId, string WidgetId)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        window.Widgets.RemoveAt(index);
        window.WidgetIndex.Remove(WidgetId);

        for (int i = 0; i < window.Widgets.Count; i++)
            window.WidgetIndex[window.Widgets[i].Id] = i;
    }

    /// <summary>Replaces the displayed text of a label.</summary>
    private void SetLabelInternal(string WindowId, string WidgetId, string Text)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.Text = Text;
        window.Widgets[index] = widget;
    }

    /// <summary>Updates a slider's displayed numeric value.</summary>
    private void SetSliderValueInternal(string WindowId, string WidgetId, float Value)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.SliderValue = global::System.Math.Clamp(Value, widget.SliderMin, widget.SliderMax);
        window.Widgets[index] = widget;
    }

    /// <summary>Updates a toggle's displayed boolean value.</summary>
    private void SetToggleValueInternal(string WindowId, string WidgetId, bool Value)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.ToggleValue = Value;
        window.Widgets[index] = widget;
    }

    /// <summary>Updates the selected dropdown index.</summary>
    private void SetDropdownValueInternal(string WindowId, string WidgetId, int Index)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int widgetIdx)) return;

        var widget = window.Widgets[widgetIdx];

        if (widget.DropdownOptions != null && Index >= 0 && Index < widget.DropdownOptions.Length)
        {
            widget.DropdownSelected = Index;
            window.Widgets[widgetIdx] = widget;
        }
    }

    /// <summary>Replaces a dropdown's available choices.</summary>
    private void SetDropdownOptionsInternal(string WindowId, string WidgetId, string[] Options)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int widgetIdx)) return;

        var widget = window.Widgets[widgetIdx];
        widget.DropdownOptions = Options;
        if (widget.DropdownSelected >= Options.Length) widget.DropdownSelected = 0;
        window.Widgets[widgetIdx] = widget;
    }

    /// <summary>Enables or disables interaction with a named widget.</summary>
    private void SetWidgetEnabledInternal(string WindowId, string WidgetId, bool Enabled)
    {
        if (!Windows.TryGetValue(WindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(WidgetId, out int index)) return;

        var widget = window.Widgets[index];
        widget.Disabled = !Enabled;
        window.Widgets[index] = widget;
    }

    /// <summary>Orders inspector windows for drawing and input hit testing.</summary>
    private void SortRenderOrder() => RenderOrder.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));

    /// <summary>Moves a window above the other inspector windows.</summary>
    private void BringToFront(WindowData Window)
    {
        Window.ZOrder = NextZOrder++;
        SortRenderOrder();
    }

    /// <summary> Registers built-in themes, creates the Inspector window, and sets up default widgets. </summary>
    public override void Initialize()
    {
        LineHeight = Renderer.GetLineHeight("Inter", FontSize);
        PrevMouse = Controls.Mouse;
        PrevKeyState = Controls.Keyboard;

        RegisterBuiltInThemes();

        UIScale = ComputeAutoScale();
        CreateWindow("inspector", "Inspector", 0);
        AddSlider("inspector", "uiScale", "UI Scale", 0.5f, 3.0f, UIScale, (value) => UIScale = value);

        int defaultThemeIndex = ThemeNameList.IndexOf("Carbon");
        ApplyTheme(defaultThemeIndex);
        AddDropdown("inspector", "theme", "Theme", GetThemeNames(), defaultThemeIndex, ApplyTheme);

        InitializeMenuBar();
    }

    /// <summary>Inserts typed characters into the currently focused text field.</summary>
    private void OnTextInput(char character)
    {
        if (!GlobalVisible || FocusedTextInputWindowId == null) return;
        if (!Windows.TryGetValue(FocusedTextInputWindowId, out var window)) return;
        if (!window.Visible) return;
        if (!window.WidgetIndex.TryGetValue(FocusedTextInputWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        string value = widget.TextInputValue ?? "";
        int cursor = Math.Clamp(widget.TextInputCursor, 0, value.Length);

        if (widget.TextInputSelectAll && (character >= ' ' || character == '\b' || character == 127)) { value = ""; cursor = 0; widget.TextInputSelectAll = false; }

        if (character == '\b')
        {
            if (cursor > 0)
            {
                value = value.Remove(cursor - 1, 1);
                cursor--;
            }
        }
        else if (character == 127)
        {
            if (cursor < value.Length) value = value.Remove(cursor, 1);
        }
        else if (character == '\r' || character == '\n')
        {
            widget.TextInputCallback?.Invoke(value);
            widget.TextInputValue = value;
            widget.TextInputCursor = cursor;
            window.Widgets[index] = widget;
            ClearTextInputFocus();

            return;
        }
        else if (character == '\t' || character == '\x1b')
        {
            if (character == '\x1b') ClearTextInputFocus();
            return;
        }
        else if (!char.IsControl(character))
        {
            value = value.Insert(cursor, character.ToString());
            cursor++;
        }

        widget.TextInputValue = value;
        widget.TextInputCursor = cursor;
        window.Widgets[index] = widget;
        CursorBlinkTimer = 0;
        widget.TextInputCallback?.Invoke(value);
    }

    /// <summary> Recomputes UI scale and triggers layout recalculation on window resize. </summary>
    public override void OnResize()
    {
        UIScale = ComputeAutoScale();
        SetSliderValue("inspector", "uiScale", UIScale);
        LayoutDone = false;
    }

    /// <summary> Processes F1 toggle, mouse input, window dragging, slider dragging, and dropdown interaction. </summary>
    public override void Update()
    {
        if (CurrentWorkspaceMode == WorkspaceMode.Windows && !LayoutDone && !Dragging && !Resizing)
        {
            AutoPositionAll();
            LayoutDone = true;
        }

        UpdateSystemsInspector();
        UpdateEntityInspector();
        UpdateProperties();
        ComputeAllLayouts();
        UpdateDebugger();
        UpdatePresentationViewport();
        UpdateCameraNavigation();

        var keyboard = Controls.Keyboard;

        if (keyboard.IsKeyDown(Key.F1) && PrevKeyState.IsKeyUp(Key.F1))
        {
            GlobalVisible = !GlobalVisible;
            UpdatePresentationViewport();

            if (!GlobalVisible) { DragStack = null; CancelTabDrag(); ClosePanelMenu(); CloseMenuDropdown(); CloseDropdown(); ClearTextInputFocus(); MouseOverUI = false; }
        }

        if (!GlobalVisible)
        { PrevKeyState = keyboard; PrevMouse = Controls.Mouse; return; }

        CursorBlinkTimer += Time.DeltaSeconds;
        foreach (char character in Controls.Text) OnTextInput(character);
        UpdateTextInputKeys(keyboard);
        var currentMouse = Controls.Mouse;
        var virtualMouse = ScreenToVirtual(new Vector2(currentMouse.X, currentMouse.Y));

        bool leftPressed = currentMouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released;
        bool leftHeld = currentMouse.LeftButton == ButtonState.Pressed;
        bool leftReleased = currentMouse.LeftButton == ButtonState.Released && PrevMouse.LeftButton == ButtonState.Pressed;

        MouseOverUI = CurrentWorkspaceMode == WorkspaceMode.Panels && !GameViewport.Contains(virtualMouse);

        if (UpdateDockTabDrag(virtualMouse, leftHeld))
        {
            PrevKeyState = keyboard;
            PrevMouse = currentMouse;
            return;
        }

        if (UpdatePanelMenu(virtualMouse, leftPressed, keyboard))
        { PrevKeyState = keyboard; PrevMouse = currentMouse; return; }

        bool menuHandled = UpdateMenuBar(virtualMouse, leftPressed, keyboard);
        PrevKeyState = keyboard;
        if (menuHandled)
        {
            PrevMouse = currentMouse;
            return;
        }

        if (UpdateDockInput(virtualMouse, leftPressed, currentMouse.ScrollWheelValue - PrevMouse.ScrollWheelValue))
        { PrevMouse = currentMouse; return; }
        if (CurrentWorkspaceMode == WorkspaceMode.Windows && UpdateWindowScroll(virtualMouse, currentMouse.ScrollWheelValue - PrevMouse.ScrollWheelValue))
        { PrevMouse = currentMouse; return; }

        if (DraggingSlider && leftHeld)
        {
            float prevScale = UIScale;
            HandleSliderDrag(virtualMouse);
            if (UIScale != prevScale) LayoutDone = false;
            MouseOverUI = true;
        }
        else if (DraggingSlider && leftReleased)
        {
            DraggingSlider = false;
        }

        if (!Renderer.IsActive || keyboard.IsKeyDown(Key.Escape)) Dragging = Resizing = false;

        if (Dragging && leftHeld)
        {
            if (Windows.TryGetValue(DragWindowId, out var dragWin))
            {
                float screenW = Renderer.VirtualWidth / UIScale;
                float screenH = Renderer.VirtualHeight / UIScale;
                var pos = virtualMouse - DragOffset;
                pos.X = global::System.Math.Clamp(pos.X, -dragWin.Size.X + Padding * 4, screenW - Padding * 4);
                pos.Y = global::System.Math.Clamp(pos.Y, MenuBarHeight, screenH - TitleBarHeight);
                dragWin.Position = pos;
            }

            MouseOverUI = true;
        }
        else if (Dragging && leftReleased)
        {
            Dragging = false;
        }

        if (Resizing && leftHeld)
        {
            if (Windows.TryGetValue(ResizeWindowId, out var resizeWin))
            {
                float screenW = Renderer.VirtualWidth / UIScale;
                float screenH = Renderer.VirtualHeight / UIScale;
                float deltaX = virtualMouse.X - ResizeStartMouse.X;
                float deltaY = virtualMouse.Y - ResizeStartMouse.Y;
                float maxW = Math.Max(1, screenW - resizeWin.Position.X);
                float maxH = Math.Max(1, screenH - resizeWin.Position.Y);
                resizeWin.Size = new Vector2(Math.Clamp(ResizeStartWidth + deltaX, Math.Min(MinWindowWidth, maxW), maxW), resizeWin.Size.Y);
                resizeWin.ResizedHeight = Math.Clamp(ResizeStartHeight + deltaY, Math.Min(MinWindowHeight, maxH), maxH);
            }

            MouseOverUI = true;
        }
        else if (Resizing && leftReleased)
        {
            Resizing = false;
        }

        if (DraggingListBox && leftHeld)
        {
            if (Windows.TryGetValue(ListBoxDragWindowId, out var listDragWindow) &&
                listDragWindow.WidgetIndex.TryGetValue(ListBoxDragWidgetId, out int listDragIdx))
            {
                var listDragWidget = listDragWindow.Widgets[listDragIdx];

                if (listDragWidget.ListBoxItems != null && listDragWidget.ListBoxItems.Length > 0)
                {
                    int itemHeight = (int)(LineHeight + 4);
                    int headerOffset = listDragWidget.ListBoxHeader != null ? itemHeight + 2 : 0;
                    int contentY = listDragWidget.Bounds.Y + 4 + headerOffset;
                    int localY = (int)virtualMouse.Y - contentY;
                    int currentIndex = Math.Clamp(localY / itemHeight + listDragWidget.ListBoxScroll, 0, listDragWidget.ListBoxItems.Length - 1);

                    if (currentIndex != ListBoxDragStartIndex)
                    {
                        int rangeStart = Math.Min(ListBoxDragStartIndex, currentIndex);
                        int rangeEnd = Math.Max(ListBoxDragStartIndex, currentIndex);
                        listDragWidget.ListBoxSelected.Clear();
                        if (ListBoxDragBaseSelection != null)
                            foreach (int baseIdx in ListBoxDragBaseSelection)
                                listDragWidget.ListBoxSelected.Add(baseIdx);

                        for (int rangeIdx = rangeStart; rangeIdx <= rangeEnd; rangeIdx++)
                        {
                            if (listDragWidget.ListBoxItems[rangeIdx].Length > 0 && listDragWidget.ListBoxItems[rangeIdx][0] == '\x03') continue;

                            listDragWidget.ListBoxSelected.Add(rangeIdx);
                        }

                        listDragWindow.Widgets[listDragIdx] = listDragWidget;
                    }
                }
            }

            MouseOverUI = true;
        }
        else if (DraggingListBox && leftReleased)
        {
            DraggingListBox = false;
        }

        if (DraggingScrollbar && leftHeld)
        {
            if (Windows.TryGetValue(ScrollbarDragWindowId, out var scrollWindow) &&
                scrollWindow.WidgetIndex.TryGetValue(ScrollbarDragWidgetId, out int scrollIdx))
            {
                var scrollWidget = scrollWindow.Widgets[scrollIdx];

                if (scrollWidget.ListBoxItems != null && scrollWidget.ListBoxItems.Length > 0)
                {
                    int itemHeight = (int)(LineHeight + 4);
                    int headerOffset = scrollWidget.ListBoxHeader != null ? itemHeight + 2 : 0;
                    int maxVisible = (scrollWidget.Bounds.Height - 8 - headerOffset) / itemHeight;
                    int trackY = scrollWidget.Bounds.Y + 4 + headerOffset;
                    int trackHeight = scrollWidget.Bounds.Height - 8 - headerOffset;
                    float dragRatio = Math.Clamp(((float)virtualMouse.Y - trackY) / trackHeight, 0f, 1f);
                    int maxScroll = Math.Max(0, scrollWidget.ListBoxItems.Length - maxVisible);
                    scrollWidget.ListBoxScroll = (int)(dragRatio * maxScroll);
                    scrollWindow.Widgets[scrollIdx] = scrollWidget;
                }
            }

            MouseOverUI = true;
        }
        else if (DraggingScrollbar && leftReleased)
        {
            DraggingScrollbar = false;
        }

        ComputeAllLayouts();

        int scrollDelta = currentMouse.ScrollWheelValue - PrevMouse.ScrollWheelValue;

        if (OpenDropdownWindowId != null)
        {
            if (scrollDelta != 0 && OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
            {
                int maxScroll = Math.Max(0, DropdownTotalOptions - MaxVisibleDropdownItems);
                DropdownScrollOffset = Math.Clamp(DropdownScrollOffset - Math.Sign(scrollDelta), 0, maxScroll);
                MouseOverUI = true;
            }
        }

        if (scrollDelta != 0 && OpenDropdownWindowId == null) HandleListBoxScroll(virtualMouse, scrollDelta);

        if (!Dragging && !DraggingSlider && !Resizing && OpenDropdownWindowId != null && leftPressed)
        {
            if (OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
            {
                MouseOverUI = true;
                HandleDropdownPopupClick(virtualMouse);
                PrevMouse = currentMouse;

                return;
            }

            CloseDropdown();
            MouseOverUI = true;
            PrevMouse = currentMouse;

            return;
        }

        if (!Dragging && !DraggingSlider && !Resizing)
        {
            for (int i = RenderOrder.Count - 1; i >= 0; i--)
            {
                var window = RenderOrder[i];

                if (!IsWindowDisplayed(window)) continue;
                if (!window.WindowBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y)) continue;

                MouseOverUI = true;

                if (leftPressed)
                {
                    if (window.CloseBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
                    {
                        SetWindowVisible(window.Id, false);
                        break;
                    }

                    if (CurrentWorkspaceMode == WorkspaceMode.Windows && window.TitleBarBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
                    {
                        Dragging = true;
                        DragWindowId = window.Id;
                        DragOffset = virtualMouse - window.Position;
                        BringToFront(window);
                        break;
                    }

                    if (CurrentWorkspaceMode == WorkspaceMode.Windows && window.Resizable)
                    {
                        var resizeBounds = new Rectangle(
                            window.WindowBounds.Right - ResizeHandleSize,
                            window.WindowBounds.Bottom - ResizeHandleSize,
                            ResizeHandleSize, ResizeHandleSize);

                        if (resizeBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y))
                        {
                            Resizing = true;
                            ResizeWindowId = window.Id;
                            ResizeStartMouse = virtualMouse;
                            ResizeStartWidth = window.Size.X;
                            ResizeStartHeight = window.ResizedHeight > 0 ? window.ResizedHeight : ComputeWindowHeight(window);
                            BringToFront(window);
                            break;
                        }
                    }

                    BringToFront(window);
                    ClearTextInputFocus();
                    HandleWidgetPress(window, virtualMouse);
                    break;
                }

                break;
            }

            if (leftPressed && !MouseOverUI) ClearTextInputFocus();
        }

        if (OpenDropdownWindowId != null && OpenDropdownPopupBounds.Contains((int)virtualMouse.X, (int)virtualMouse.Y)) MouseOverUI = true;

        PrevMouse = currentMouse;
    }

    /// <summary>Handles text editing shortcuts, navigation, and key repetition.</summary>
    private void UpdateTextInputKeys(KeyboardState Keyboard)
    {
        if (FocusedTextInputWindowId == null) return;
        if (!Windows.TryGetValue(FocusedTextInputWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(FocusedTextInputWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        string value = widget.TextInputValue ?? "";
        int cursor = Math.Clamp(widget.TextInputCursor, 0, value.Length);
        bool changed = false;

        if ((Keyboard.IsKeyDown(Key.LeftControl) || Keyboard.IsKeyDown(Key.RightControl)) && Keyboard.IsKeyDown(Key.A) && PrevKeyState.IsKeyUp(Key.A))
        { widget.TextInputSelectAll = true; window.Widgets[index] = widget; }

        if (Keyboard.IsKeyDown(Key.Left) && PrevKeyState.IsKeyUp(Key.Left) && cursor > 0)
        { cursor--; changed = true; }

        if (Keyboard.IsKeyDown(Key.Right) && PrevKeyState.IsKeyUp(Key.Right) && cursor < value.Length)
        { cursor++; changed = true; }

        if (Keyboard.IsKeyDown(Key.Home) && PrevKeyState.IsKeyUp(Key.Home))
        { cursor = 0; changed = true; }

        if (Keyboard.IsKeyDown(Key.End) && PrevKeyState.IsKeyUp(Key.End))
        { cursor = value.Length; changed = true; }

        if (changed)
        {
            widget.TextInputSelectAll = false;
            widget.TextInputCursor = cursor;
            window.Widgets[index] = widget;
            CursorBlinkTimer = 0;
        }
    }

    /// <summary>Assigns keyboard focus and cursor state to a text field.</summary>
    private void FocusTextInput(string WindowId, string WidgetId)
    {
        FocusedTextInputWindowId = WindowId;
        FocusedTextInputWidgetId = WidgetId;
        CursorBlinkTimer = 0;
    }

    /// <summary>Ends text editing and clears the focused field.</summary>
    private void ClearTextInputFocus()
    {
        FocusedTextInputWindowId = null;
        FocusedTextInputWidgetId = null;
    }

    /// <summary>Reports whether the named text field currently owns keyboard focus.</summary>
    private bool IsTextInputFocused(string WindowId, string WidgetId)
        => FocusedTextInputWindowId == WindowId && FocusedTextInputWidgetId == WidgetId;

    /// <summary>Dispatches a pointer press to the widget under the cursor.</summary>
    private void HandleWidgetPress(WindowData Window, Vector2 Mouse)
    {
        if (CurrentWorkspaceMode == WorkspaceMode.Panels && Mouse.Y < Window.TitleBarBounds.Bottom) return;
        for (int i = 0; i < Window.Widgets.Count; i++)
        {
            var widget = Window.Widgets[i];

            if (!widget.Visible || widget.Disabled) continue;
            if (!widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y)) continue;

            switch (widget.Type)
            {
                case WidgetType.Slider:
                    int trackLeft = widget.Bounds.X + 4;
                    int trackWidth = widget.Bounds.Width - 8;
                    float range = widget.SliderMax - widget.SliderMin;
                    float normalizedValue = range > 0 ? (widget.SliderValue - widget.SliderMin) / range : 0;
                    int fillWidth = (int)(trackWidth * normalizedValue);
                    int handleCX = trackLeft + fillWidth;
                    int handleCY = (int)(widget.Bounds.Y + LineHeight + 10) + SliderTrackHeight / 2;
                    float dx = Mouse.X - handleCX;
                    float dy = Mouse.Y - handleCY;

                    if (dx * dx + dy * dy > SliderHandleSize * SliderHandleSize) return;

                    DraggingSlider = true;
                    SliderWindowId = Window.Id;
                    SliderWidgetId = widget.Id;

                    return;

                case WidgetType.Toggle:
                    widget.ToggleValue = !widget.ToggleValue;
                    Window.Widgets[i] = widget;
                    widget.ToggleCallback?.Invoke(widget.ToggleValue);

                    return;

                case WidgetType.Button:
                    widget.ButtonCallback?.Invoke();
                    return;

                case WidgetType.TextInput:
                    string textValue = widget.TextInputValue ?? "";

                    if (textValue.Length > 0)
                    {
                        int clearIconSize = widget.Bounds.Height - 36;
                        int clearIconPad = 12;
                        int clearX = widget.Bounds.Right - clearIconPad - clearIconSize;

                        if (Mouse.X >= clearX)
                        {
                            widget.TextInputValue = "";
                            widget.TextInputCursor = 0;
                            Window.Widgets[i] = widget;
                            widget.TextInputCallback?.Invoke("");
                            ClearTextInputFocus();

                            return;
                        }
                    }

                    FocusTextInput(Window.Id, widget.Id);
                    widget.TextInputCursor = (widget.TextInputValue ?? "").Length;
                    Window.Widgets[i] = widget;

                    return;

                case WidgetType.ListBox:
                    HandleListBoxClick(Window, i, Mouse);
                    return;

                case WidgetType.Dropdown:
                    if (OpenDropdownWindowId == Window.Id && OpenDropdownWidgetId == widget.Id)
                        CloseDropdown();
                    else
                        OpenDropdown(Window.Id, widget.Id, widget);
                    return;
            }
        }
    }

    private const int ScrollbarTrackWidth = 18;

    /// <summary>Updates list selection or invokes a row toggle at the pointer position.</summary>
    private void HandleListBoxClick(WindowData Window, int WidgetIndex, Vector2 Mouse)
    {
        var widget = Window.Widgets[WidgetIndex];

        if (widget.ListBoxItems == null || widget.ListBoxItems.Length == 0) return;

        int itemHeight = (int)(LineHeight + 4);
        int headerOffset = widget.ListBoxHeader != null ? itemHeight + 2 : 0;
        int maxVisible = (widget.Bounds.Height - 8 - headerOffset) / itemHeight;
        int scrollClamp = Math.Max(0, widget.ListBoxItems.Length - maxVisible);

        if (widget.ListBoxScroll > scrollClamp)
        {
            widget.ListBoxScroll = scrollClamp;
            Window.Widgets[WidgetIndex] = widget;
        }

        if (widget.ListBoxItems.Length > maxVisible && Mouse.X >= widget.Bounds.Right - ScrollbarTrackWidth)
        {
            int trackY = widget.Bounds.Y + 4 + headerOffset;
            int trackHeight = widget.Bounds.Height - 8 - headerOffset;
            float clickRatio = Math.Clamp(((float)Mouse.Y - trackY) / trackHeight, 0f, 1f);
            int maxScroll = Math.Max(0, widget.ListBoxItems.Length - maxVisible);
            widget.ListBoxScroll = (int)(clickRatio * maxScroll);
            Window.Widgets[WidgetIndex] = widget;
            DraggingScrollbar = true;
            ScrollbarDragWindowId = Window.Id;
            ScrollbarDragWidgetId = widget.Id;

            return;
        }

        int contentY = widget.Bounds.Y + 4 + headerOffset;
        int localY = (int)Mouse.Y - contentY;

        if (localY < 0) return;

        int clickedIndex = localY / itemHeight + widget.ListBoxScroll;

        if (clickedIndex < 0 || clickedIndex >= widget.ListBoxItems.Length) return;
        if (widget.ListBoxItems[clickedIndex].Length > 0 && widget.ListBoxItems[clickedIndex][0] == '\x03') return;

        if (widget.ListBoxToggleCallback != null)
        {
            string itemText = widget.ListBoxItems[clickedIndex];
            bool hasStatus = itemText.Length > 0 && (itemText[0] == '\x01' || itemText[0] == '\x02' || itemText[0] == '\x04' || itemText[0] == '\x05' || itemText[0] == '\x06' || itemText[0] == '\x07');

            if (hasStatus)
            {
                bool isIndented = itemText[0] == '\x04' || itemText[0] == '\x05';
                int indent = isIndented ? 16 : 0;
                float controlEndX = widget.Bounds.X + Padding + indent + 30;

                if (Mouse.X <= controlEndX)
                {
                    widget.ListBoxToggleCallback.Invoke(clickedIndex);
                    return;
                }
            }
        }

        widget.ListBoxSelected ??= new HashSet<int>();

        var keyboard = Controls.Keyboard;
        bool ctrlHeld = keyboard.IsKeyDown(Key.LeftControl) || keyboard.IsKeyDown(Key.RightControl);

        if (ctrlHeld)
        {
            if (widget.ListBoxSelected.Contains(clickedIndex))
                widget.ListBoxSelected.Remove(clickedIndex);
            else
                widget.ListBoxSelected.Add(clickedIndex);
        }
        else
        {
            widget.ListBoxSelected.Clear();
            widget.ListBoxSelected.Add(clickedIndex);
        }

        Window.Widgets[WidgetIndex] = widget;

        DraggingListBox = true;
        ListBoxDragWindowId = Window.Id;
        ListBoxDragWidgetId = widget.Id;
        ListBoxDragStartIndex = clickedIndex;
        ListBoxDragBaseSelection = new HashSet<int>(widget.ListBoxSelected);
    }

    /// <summary>Adjusts list scroll position within its content bounds.</summary>
    private void HandleListBoxScroll(Vector2 Mouse, int ScrollDelta)
    {
        for (int i = RenderOrder.Count - 1; i >= 0; i--)
        {
            var window = RenderOrder[i];

            if (!IsWindowDisplayed(window)) continue;
            if (CurrentWorkspaceMode == WorkspaceMode.Panels && (!window.WindowBounds.Contains(Mouse) || Mouse.Y < window.TitleBarBounds.Bottom)) continue;

            for (int j = 0; j < window.Widgets.Count; j++)
            {
                var widget = window.Widgets[j];

                if (widget.Type != WidgetType.ListBox || !widget.Visible) continue;
                if (!widget.Bounds.Contains((int)Mouse.X, (int)Mouse.Y)) continue;

                int itemHeight = (int)(LineHeight + 4);
                int headerOffset = widget.ListBoxHeader != null ? itemHeight + 2 : 0;
                int maxVisible = (widget.Bounds.Height - 8 - headerOffset) / itemHeight;
                int itemCount = widget.ListBoxItems?.Length ?? 0;
                int maxScroll = Math.Max(0, itemCount - maxVisible);
                widget.ListBoxScroll = Math.Clamp(widget.ListBoxScroll - Math.Sign(ScrollDelta), 0, maxScroll);
                window.Widgets[j] = widget;
                MouseOverUI = true;

                return;
            }
        }
    }

    /// <summary>Maps pointer movement to a bounded slider value.</summary>
    private void HandleSliderDrag(Vector2 Mouse)
    {
        if (!Windows.TryGetValue(SliderWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(SliderWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        int trackLeft = widget.Bounds.X + 4;
        int trackRight = widget.Bounds.Right - 4;
        int trackWidth = trackRight - trackLeft;

        if (trackWidth <= 0) return;

        float normalizedValue = global::System.Math.Clamp((Mouse.X - trackLeft) / trackWidth, 0f, 1f);
        widget.SliderValue = widget.SliderMin + normalizedValue * (widget.SliderMax - widget.SliderMin);
        window.Widgets[index] = widget;
        widget.SliderCallback?.Invoke(widget.SliderValue);
    }

    /// <summary>Opens the option popup for a dropdown widget.</summary>
    private void OpenDropdown(string WindowId, string WidgetId, Widget Widget)
    {
        OpenDropdownWindowId = WindowId;
        OpenDropdownWidgetId = WidgetId;
        DropdownScrollOffset = 0;

        int optionCount = Widget.DropdownOptions?.Length ?? 0;
        DropdownTotalOptions = optionCount;

        int visibleCount = Math.Min(optionCount, MaxVisibleDropdownItems);
        int height = visibleCount * WidgetHeight;
        int screenWidth = (int)(Renderer.VirtualWidth / UIScale);
        int screenHeight = (int)(Renderer.VirtualHeight / UIScale);
        int width = Math.Min(Widget.Bounds.Width, screenWidth);
        int x = Math.Clamp(Widget.Bounds.X, 0, Math.Max(0, screenWidth - width));
        int y = Widget.Bounds.Bottom + height <= screenHeight ? Widget.Bounds.Bottom : Widget.Bounds.Top - height;
        OpenDropdownPopupBounds = new Rectangle(x, Math.Max(MenuBarHeight, y), width, height);
    }

    /// <summary>Closes the active widget dropdown popup.</summary>
    private void CloseDropdown()
    {
        OpenDropdownWindowId = null;
        OpenDropdownWidgetId = null;
        DropdownScrollOffset = 0;
    }

    /// <summary>Selects the clicked dropdown option or dismisses the popup.</summary>
    private void HandleDropdownPopupClick(Vector2 Mouse)
    {
        if (!Windows.TryGetValue(OpenDropdownWindowId, out var window)) return;
        if (!window.WidgetIndex.TryGetValue(OpenDropdownWidgetId, out int index)) return;

        var widget = window.Widgets[index];
        int localIndex = ((int)Mouse.Y - OpenDropdownPopupBounds.Y) / WidgetHeight;
        int optionIndex = localIndex + DropdownScrollOffset;

        if (widget.DropdownOptions != null && optionIndex >= 0 && optionIndex < widget.DropdownOptions.Length)
        {
            widget.DropdownSelected = optionIndex;
            window.Widgets[index] = widget;
            widget.DropdownCallback?.Invoke(optionIndex);
        }

        CloseDropdown();
    }

    /// <summary> Disposes blur render targets and clears the singleton instance. </summary>
    public override void Dispose()
    {
        WorkspaceSnapshot?.Dispose();
        DockSurface?.Dispose();
        if (Renderer != null) Renderer.ScenePresentationBounds = null;
        BlurRT_A?.Dispose();
        BlurRT_B?.Dispose();
        if (Instance == this) Instance = null;
    }
}
