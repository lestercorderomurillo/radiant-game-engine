using System;
using System.Linq;
using System.Reflection;
using com.radiant.engine.core;
using com.radiant.engine.graphics;

internal static class GraphicsResourceRegression
{
    public static void Run()
    {
        var assembly = typeof(Renderer).Assembly;
        string[] names = { "Texture", "Texture2D", "RenderTarget2D", "RenderTargetBinding", "Effect", "EffectParameter", "EffectTechnique", "EffectPass", "SurfaceFormat", "DepthFormat", "RenderTargetUsage" };
        bool NativeResource(Type type) =>
            (type.Namespace == "Microsoft.Xna.Framework.Graphics" && names.Contains(type.Name)) ||
            (type.HasElementType && NativeResource(type.GetElementType())) ||
            (type.IsGenericType && type.GetGenericArguments().Any(NativeResource));
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var field in type.GetFields(flags)) Require(!NativeResource(field.FieldType), $"Native resource field: {type}.{field.Name}");
            foreach (var property in type.GetProperties(flags)) Require(!NativeResource(property.PropertyType), $"Native resource property: {type}.{property.Name}");
            foreach (var method in type.GetMethods(flags))
            {
                Require(!NativeResource(method.ReturnType), $"Native resource return: {type}.{method.Name}");
                foreach (var parameter in method.GetParameters()) Require(!NativeResource(parameter.ParameterType), $"Native resource input: {type}.{method.Name}");
            }
            foreach (var constructor in type.GetConstructors())
                foreach (var parameter in constructor.GetParameters()) Require(!NativeResource(parameter.ParameterType), $"Native constructor: {type}");
        }
        Require(typeof(IGraphicsResourceFactory).IsAssignableFrom(typeof(Renderer)), "Renderer must implement the resource factory contract");
        foreach (var type in new[] { typeof(Texture2D), typeof(RenderTarget2D), typeof(Effect) })
        {
            Require(type.IsAbstract && typeof(IGraphicsResource).IsAssignableFrom(type), "Resource must be backend-implementable: " + type);
            Require(type.BaseType.Assembly != typeof(Microsoft.Xna.Framework.Graphics.Texture2D).Assembly, "Native base class: " + type);
        }
        var adapter = typeof(com.radiant.engine.runtime.DesktopBackend).Assembly.GetType("com.radiant.engine.runtime.MonoGameResources", true);
        foreach (var type in new[] { typeof(SurfaceFormat), typeof(DepthFormat), typeof(RenderTargetUsage) })
        {
            var convert = adapter.GetMethod("ToNative", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { type }, null);
            foreach (var value in Enum.GetValues(type))
            {
                var native = convert.Invoke(null, new[] { value });
                Require(native.ToString() == value.ToString(), "Format mapping: " + value);
                var inverse = adapter.GetMethod("FromNative", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { native.GetType() }, null);
                Require(Equals(value, inverse.Invoke(null, new[] { native })), "Format round trip: " + value);
            }
            try { convert.Invoke(null, new[] { Enum.ToObject(type, -1) }); throw new Exception("Invalid format accepted"); }
            catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException) { }
        }
        Console.WriteLine("PASS: Radiant resource contracts, no exported native resource signatures, checked format mappings.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
