using System;
using System.Globalization;
using System.Reflection;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

/// <summary>On-demand editing of public component data; never stores archetype references.</summary>
public static class ComponentEditor
{
    public static string[] Axes(Type type) => type == typeof(Vector2) ? ["X", "Y"] : type == typeof(Vector3) ? ["X", "Y", "Z"] : type == typeof(Vector4) ? ["X", "Y", "Z", "W"] : type == typeof(Color) ? ["R", "G", "B", "A"] : type == typeof(Point) ? ["X", "Y"] : ["Value"];
    public static bool Supports(Type type) => type.IsEnum || type == typeof(bool) || type == typeof(string) || type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Color) || type == typeof(Point);
    public static string Read(object value, int axis = 0)
    {
        object scalar = value switch { Vector2 v => axis == 0 ? v.X : v.Y, Vector3 v => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z, Vector4 v => axis == 0 ? v.X : axis == 1 ? v.Y : axis == 2 ? v.Z : v.W, Color c => axis == 0 ? c.R : axis == 1 ? c.G : axis == 2 ? c.B : c.A, Point p => axis == 0 ? p.X : p.Y, _ => value };
        return scalar is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : scalar?.ToString() ?? "";
    }
    public static bool TrySetField(ECS ecs, int entity, Type type, string name, string text, int axis, out string error)
    {
        error = "Entity or component unavailable.";
        if (!ecs.TryGetComponent(entity, type, out var component)) return false;
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        if (field == null || field.IsInitOnly || !Supports(field.FieldType) || axis < 0 || axis >= Axes(field.FieldType).Length) { error = "Field is read-only or unsupported."; return false; }
        try
        {
            object value = field.GetValue(component);
            if (value is Vector2 v2) { float n = Number(text); if (axis == 0) v2.X = n; else v2.Y = n; value = v2; }
            else if (value is Vector3 v3) { float n = Number(text); if (axis == 0) v3.X = n; else if (axis == 1) v3.Y = n; else v3.Z = n; value = v3; }
            else if (value is Vector4 v4) { float n = Number(text); if (axis == 0) v4.X = n; else if (axis == 1) v4.Y = n; else if (axis == 2) v4.Z = n; else v4.W = n; value = v4; }
            else if (value is Color color) { byte n = byte.Parse(text, CultureInfo.InvariantCulture); if (axis == 0) color.R = n; else if (axis == 1) color.G = n; else if (axis == 2) color.B = n; else color.A = n; value = color; }
            else if (value is Point point) { int n = int.Parse(text, CultureInfo.InvariantCulture); if (axis == 0) point.X = n; else point.Y = n; value = point; }
            else if (field.FieldType.IsEnum) value = Enum.Parse(field.FieldType, text);
            else if (field.FieldType == typeof(string)) value = text;
            else value = Convert.ChangeType(text, field.FieldType, CultureInfo.InvariantCulture);
            if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d)) throw new FormatException();
            field.SetValue(component, value);
            if (component is Camera3D camera && !Valid(camera) || component is Camera2D camera2D && (!float.IsFinite(camera2D.Zoom) || camera2D.Zoom <= 0)) { error = "Invalid camera pose or lens settings."; return false; }
            error = ecs.TrySetComponent(entity, component) ? "" : "Component was removed.";
            return error.Length == 0;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException) { error = "Enter a valid, finite value."; return false; }
    }
    private static float Number(string text)
    {
        float value = float.Parse(text, CultureInfo.InvariantCulture);
        return float.IsFinite(value) ? value : throw new FormatException();
    }
    private static bool Valid(Camera3D camera)
    {
        float cross = Vector3.Cross(camera.Target - camera.Position, camera.Up).LengthSquared();
        return camera.NearPlane > 0 && camera.FarPlane > camera.NearPlane && camera.FieldOfView > 0 && camera.FieldOfView < MathHelper.Pi && float.IsFinite(cross) && cross > 1e-12f;
    }
}
