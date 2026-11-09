using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

public partial class Inspector
{
    private int InspectedEntity = -1;
    private Type[] PropertyComponents = Array.Empty<Type>();
    private FieldInfo[] PropertyFields = Array.Empty<FieldInfo>();
    private Type PropertyType;
    private FieldInfo PropertyField;
    private string[] PropertyAxes = Array.Empty<string>();
    private float EditStep = 0.1f;
    private int EditAxis;

    public static int SelectedEntity => Instance?.InspectedEntity ?? -1;
    public static bool IsEditingText => Instance?.GlobalVisible == true && Instance.FocusedTextInputWindowId != null && Instance.Windows.TryGetValue(Instance.FocusedTextInputWindowId, out var window) && window.Visible;

    /// <summary>Selects an entity and opens its property inspector.</summary>
    public static void SelectEntity(int entity) => Instance?.SelectEntityInternal(entity);

    /// <summary>Synchronizes entity selection, search results, and property controls.</summary>
    private void SelectEntityInternal(int entity)
    {
        if (!Scene.ECS.IsAlive(entity)) return;

        GlobalVisible = true;
        OpenEntityInspector();
        SetTextInputValue("entity_inspector", "find_input", "");
        RefreshEntityList();

        var selected = GetListBoxSelected("entity_inspector", "results_list");
        selected.Clear();
        selected.Add(FilteredEntityIds.IndexOf(entity));
        InspectEntity(entity);
    }

    /// <summary>Refreshes displayed component values without interrupting active text edits.</summary>
    private void UpdateProperties()
    {
        if (!GlobalVisible) return;

        var selected = GetSelectedEntityIds();
        int entity = InspectedEntity;
        if (IsWindowVisibleInternal("entity_inspector")) entity = selected?.Count == 1 ? selected[0] : -1;
        if (!Scene.ECS.IsAlive(entity)) entity = -1;
        if (entity != InspectedEntity) InspectEntity(entity);

        if (entity < 0 || !IsWindowVisibleInternal("entity_properties")) return;

        var types = Scene.ECS.GetComponentTypes(entity);

        if (!types.SequenceEqual(PropertyComponents)) { InspectEntity(entity); return; }
        if (PropertyField == null || !Scene.ECS.TryGetComponent(entity, PropertyType, out var component)) return;

        var value = PropertyField.GetValue(component);

        for (int i = 0; i < PropertyAxes.Length; i++)
            if (!IsTextInputFocused("entity_properties", $"value{i}")) SetTextInputValue("entity_properties", $"value{i}", ComponentEditor.Read(value, i));

        if (value is bool boolean) SetToggleValue("entity_properties", "boolean", boolean);
        if (PropertyField.FieldType.IsEnum) SetDropdownValue("entity_properties", "enum", Array.IndexOf(Enum.GetNames(PropertyField.FieldType), value.ToString()));
    }

    /// <summary>Builds property controls for the selected live entity.</summary>
    private void InspectEntity(int entity)
    {
        InspectedEntity = entity;
        NavigatingCamera = false;
        HideWindow("camera_controls");

        if (entity < 0) { HideWindow("entity_properties"); return; }

        PropertyComponents = Scene.ECS.GetComponentTypes(entity).ToArray();
        PropertyField = null;
        CreateWindow("entity_properties", "Entity Properties", 53);

        var window = Windows["entity_properties"];
        window.Title = $"Entity {entity} Properties";
        window.Widgets.Clear();
        window.WidgetIndex.Clear();
        AddDropdown("entity_properties", "component", "Component", PropertyComponents.Select(t => t.Name).ToArray(), 0, SelectPropertyComponent);
        AddDropdown("entity_properties", "field", "Field", Array.Empty<string>(), 0, SelectPropertyField);
        AddLabel("entity_properties", "axes", "");
        SelectPropertyComponent(Math.Max(0, Array.IndexOf(PropertyComponents, PropertyType)));

        if (PropertyComponents.Length == 0) { SetLabel("entity_properties", "axes", "Entity has no components."); PropertyType = null; }

        SetDropdownValue("entity_properties", "component", Array.IndexOf(PropertyComponents, PropertyType));
        ShowWindow("entity_properties");
    }

    /// <summary>Selects the component whose editable fields are shown.</summary>
    private void SelectPropertyComponent(int index)
    {
        if (index < 0 || index >= PropertyComponents.Length) return;

        PropertyType = PropertyComponents[index];
        PropertyFields = PropertyType.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(f => !f.IsInitOnly && ComponentEditor.Supports(f.FieldType)).ToArray();
        SetDropdownOptions("entity_properties", "field", PropertyFields.Select(f => f.Name).ToArray());
        SetDropdownValue("entity_properties", "field", 0);
        SelectPropertyField(0);
    }

    /// <summary>Creates editing controls appropriate to the selected field type.</summary>
    private void SelectPropertyField(int index)
    {
        ClearTextInputFocus();
        EditAxis = 0;

        var window = Windows["entity_properties"];

        foreach (var widget in window.Widgets.Skip(3).ToArray()) RemoveWidget("entity_properties", widget.Id);

        PropertyField = index >= 0 && index < PropertyFields.Length ? PropertyFields[index] : null;

        if (PropertyField != null)
        {
            var axes = ComponentEditor.Axes(PropertyField.FieldType);
            PropertyAxes = axes;
            SetLabel("entity_properties", "axes", string.Join(" / ", axes));
            if (PropertyField.FieldType == typeof(bool)) AddToggle("entity_properties", "boolean", PropertyField.Name, false, value => EditValue(value.ToString(), 0));
            else if (PropertyField.FieldType.IsEnum) AddDropdown("entity_properties", "enum", "Value", Enum.GetNames(PropertyField.FieldType), 0, option => EditValue(Enum.GetNames(PropertyField.FieldType)[option], 0));
            else for (int i = 0; i < axes.Length; i++) { int axis = i; AddTextInput("entity_properties", $"value{i}", axes[i], text => EditValue(text, axis), axes.Length > 1 ? 1f / axes.Length : 0); if (PropertyField.FieldType != typeof(string)) MarkNumericInput("entity_properties", $"value{i}"); }

            if (PropertyField.FieldType != typeof(bool) && PropertyField.FieldType != typeof(string) && !PropertyField.FieldType.IsEnum)
            {
                if (IntegralField() && EditStep < 1) EditStep = 1;
                AddDropdown("entity_properties", "axis", "Adjust axis", axes, 0, axis => EditAxis = axis);
                AddLabel("entity_properties", "stepLabel", "Step size");
                AddTextInput("entity_properties", "step", "Step", text => { if (float.TryParse(text, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && value > 0) EditStep = value; });
                MarkNumericInput("entity_properties", "step");
                SetTextInputValue("entity_properties", "step", EditStep.ToString(CultureInfo.InvariantCulture));
                AddButton("entity_properties", "minus", "- Step", () => StepValue(-1), 0.5f);
                AddButton("entity_properties", "plus", "+ Step", () => StepValue(1), 0.5f);
            }
        }
        else SetLabel("entity_properties", "axes", "No editable public fields.");

        AddLabel("entity_properties", "status", "Changes apply immediately.");
        AddCameraControls();
    }

    /// <summary>Configures text input for numeric component editing.</summary>
    private void MarkNumericInput(string windowId, string widgetId)
    {
        var window = Windows[windowId];
        int index = window.WidgetIndex[widgetId];
        var widget = window.Widgets[index];
        widget.TextInputNumeric = true;
        window.Widgets[index] = widget;
    }

    /// <summary>Parses an edited component value and applies it when validation succeeds.</summary>
    private void EditValue(string text, int axis)
    {
        if (PropertyField == null) return;

        ComponentEditor.TrySetField(Scene.ECS, InspectedEntity, PropertyType, PropertyField.Name, text, axis, out string error);
        SetLabel("entity_properties", "status", error.Length == 0 ? "Changes apply immediately." : error);
    }

    /// <summary>Adjusts a numeric component value by the configured step.</summary>
    private void StepValue(int direction)
    {
        if (!Scene.ECS.TryGetComponent(InspectedEntity, PropertyType, out var component)) return;

        string text = ComponentEditor.Read(PropertyField.GetValue(component), EditAxis);

        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) || !decimal.TryParse(EditStep.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal step)) { SetLabel("entity_properties", "status", "Type large values directly."); return; }

        if (IntegralField()) step = Math.Max(1, decimal.Truncate(step));

        try { EditValue((value + direction * step).ToString(CultureInfo.InvariantCulture), EditAxis); }
        catch (OverflowException) { SetLabel("entity_properties", "status", "Value exceeds the step range."); }
    }

    /// <summary>Reports whether the selected field uses integral numeric values.</summary>
    private bool IntegralField() => PropertyField.FieldType != typeof(float) && PropertyField.FieldType != typeof(double) && PropertyField.FieldType != typeof(Vector2) && PropertyField.FieldType != typeof(Vector3) && PropertyField.FieldType != typeof(Vector4);
}
