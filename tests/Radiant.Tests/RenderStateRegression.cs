using System;
using System.Linq;
using System.Reflection;
using com.radiant.engine.core;
using Native = Microsoft.Xna.Framework.Graphics;

internal static class RenderStateRegression
{
    public static void Run()
    {
        var assembly = typeof(Renderer).Assembly;
        var stateTypes = new[] { typeof(BlendState), typeof(SamplerState), typeof(RasterizerState), typeof(DepthStencilState) };
        var enums = assembly.GetExportedTypes().Where(t => t.Namespace == "com.radiant.engine.graphics" && t.IsEnum).ToArray();
        var prohibited = stateTypes.Concat(enums).Select(t => typeof(Native.BlendState).Assembly.GetType("Microsoft.Xna.Framework.Graphics." + t.Name, true)).ToArray();
        bool NativeState(Type type) => prohibited.Contains(type) || (type.HasElementType && NativeState(type.GetElementType())) || (type.IsGenericType && type.GetGenericArguments().Any(NativeState));
        foreach (var type in assembly.GetExportedTypes())
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var constructor in type.GetConstructors())
                foreach (var parameter in constructor.GetParameters()) Require(!NativeState(parameter.ParameterType), $"Native state constructor: {type}");
            foreach (var field in type.GetFields(flags)) Require(!NativeState(field.FieldType), $"Native state field: {type}.{field.Name}");
            foreach (var property in type.GetProperties(flags)) Require(!NativeState(property.PropertyType), $"Native state property: {type}.{property.Name}");
            foreach (var method in type.GetMethods(flags))
            {
                Require(!NativeState(method.ReturnType), $"Native state return: {type}.{method.Name}");
                foreach (var parameter in method.GetParameters()) Require(!NativeState(parameter.ParameterType), $"Native state parameter: {type}.{method.Name}");
            }
        }
        // Exercise the actual internal adapter without exposing native conversions in the public API.
        var adapter = assembly.GetType("com.radiant.engine.runtime.MonoGameRenderStates", true);
        using var cache = (IDisposable)Activator.CreateInstance(adapter, true);
        foreach (var type in stateTypes)
        {
            var nativeType = typeof(Native.BlendState).Assembly.GetType("Microsoft.Xna.Framework.Graphics." + type.Name, true);
            using var defaults = (Native.GraphicsResource)Activator.CreateInstance(nativeType);
            Check(Activator.CreateInstance(type), defaults);
            foreach (var preset in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                var nativePreset = nativeType.GetProperty(preset.Name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?? nativeType.GetField(preset.Name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Require(nativePreset != null, "Missing native preset " + preset.Name);
                Check(preset.GetValue(null), nativePreset);
            }
        }
        foreach (var type in enums)
        {
            var convert = adapter.GetMethod("ToNative", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { type }, null);
            foreach (var value in Enum.GetValues(type)) Require(convert.Invoke(null, new[] { value }).ToString() == value.ToString(), "Enum mapping: " + value);
            try { convert.Invoke(null, new[] { Enum.ToObject(type, 123456) }); throw new Exception("Invalid enum accepted"); }
            catch (TargetInvocationException e) when (e.InnerException is ArgumentOutOfRangeException) { }
        }
        var getBlend = adapter.GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(BlendState) }, null);
        var first = (Native.BlendState)getBlend.Invoke(cache, new object[] { BlendState.AlphaBlend });
        var equivalent = getBlend.Invoke(cache, new object[] { BlendState.AlphaBlend with { } });
        Require(ReferenceEquals(first, equivalent), "Equal descriptors allocate duplicate native states");
        var custom = BlendState.AlphaBlend with { ColorSourceBlend = Blend.DestinationColor, ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Alpha };
        var customNative = (Native.BlendState)getBlend.Invoke(cache, new object[] { custom });
        Require(customNative.ColorSourceBlend == Native.Blend.DestinationColor && customNative.ColorWriteChannels == (Native.ColorWriteChannels.Red | Native.ColorWriteChannels.Alpha), "Custom state not translated");
        Require(BlendState.AlphaBlend.ColorSourceBlend == Blend.One, "Preset mutated");
        cache.Dispose();
        Require(first.IsDisposed && customNative.IsDisposed, "Cached native states leaked");
        try { getBlend.Invoke(cache, new object[] { custom }); throw new Exception("Disposed cache accepted"); }
        catch (TargetInvocationException e) when (e.InnerException is ObjectDisposedException) { }
        Console.WriteLine("PASS: Radiant render-state API, native preset parity, enum validation, immutable descriptions, cache reuse and disposal.");

        void Check(object description, object expected)
        {
            var type = description.GetType();
            var get = adapter.GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { type }, null);
            var actual = get.Invoke(cache, new[] { description });
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var nativeProperty = expected.GetType().GetProperty(property.Name);
                Require(Equals(nativeProperty.GetValue(actual), nativeProperty.GetValue(expected)), $"Preset/default mismatch: {type.Name}.{property.Name}: {nativeProperty.GetValue(actual)} vs {nativeProperty.GetValue(expected)}");
            }
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
