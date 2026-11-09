using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.math;
using Xna = Microsoft.Xna.Framework;

internal static class MathBackendRegression
{
    public static void Run()
    {
        // Verify the actual public metadata, not source aliases.
        var prohibited = new[] { typeof(Xna.Vector2), typeof(Xna.Vector3), typeof(Xna.Vector4), typeof(Xna.Matrix), typeof(Xna.Color), typeof(Xna.Point), typeof(Xna.Rectangle) };
        bool HasNativeMath(Type type) => prohibited.Contains(type) ||
            (type.HasElementType && HasNativeMath(type.GetElementType())) ||
            (type.IsGenericType && type.GetGenericArguments().Any(HasNativeMath));
        foreach (var type in typeof(Scene).Assembly.GetExportedTypes())
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(flags)) Require(!HasNativeMath(field.FieldType), $"Native math field: {type}.{field.Name}");
            foreach (var property in type.GetProperties(flags)) Require(!HasNativeMath(property.PropertyType), $"Native math property: {type}.{property.Name}");
            foreach (var method in type.GetMethods(flags))
            {
                Require(!HasNativeMath(method.ReturnType), $"Native math return: {type}.{method.Name}");
                foreach (var parameter in method.GetParameters()) Require(!HasNativeMath(parameter.ParameterType), $"Native math argument: {type}.{method.Name}");
            }
            foreach (var constructor in type.GetConstructors())
                foreach (var parameter in constructor.GetParameters()) Require(!HasNativeMath(parameter.ParameterType), $"Native math constructor: {type}");
        }
        Require(typeof(Transform3D).GetField("Position").FieldType == typeof(global::System.Numerics.Vector3), "Transform is not System.Numerics");
        Require(typeof(Transform2D).GetField("Position").FieldType == typeof(global::System.Numerics.Vector2), "2D transform is not System.Numerics");
        Require(Marshal.SizeOf<Vector2>() == 8 && Marshal.SizeOf<Vector3>() == 12 && Marshal.SizeOf<Vector4>() == 16 && Marshal.SizeOf<Matrix>() == 64, "GPU math layout changed");
        Require(Marshal.SizeOf<MeshVertex>() == 32 && Marshal.OffsetOf<MeshVertex>(nameof(MeshVertex.Normal)).ToInt32() == 12 && Marshal.OffsetOf<MeshVertex>(nameof(MeshVertex.TextureCoordinate)).ToInt32() == 24, "Mesh vertex ABI changed");

        var meshVertices = Mesh.Plane.VertexData.ToArray();
        var meshIndices = Mesh.Plane.IndexData.ToArray();
        var mesh = new Mesh(meshVertices, meshIndices);
        meshVertices[0] = default;
        meshIndices[0] = 3;
        Require(mesh.VertexData[0].Position == Mesh.Plane.VertexData[0].Position &&
            mesh.IndexData[0] == 0, "Mesh backend views must preserve the constructor snapshot");
        Require(mesh.VertexData.Length == mesh.VertexCount &&
            mesh.IndexData.Length == mesh.TriangleCount * 3, "Mesh backend view counts differ");

        Require(Marshal.SizeOf<Color>() == 4 && Marshal.OffsetOf<Color>(nameof(Color.A)).ToInt32() == 3, "RGBA8 ABI changed");
        Require(Marshal.SizeOf<Point>() == 8 && Marshal.SizeOf<Rectangle>() == 16, "Integer geometry ABI changed");
        Require(Marshal.SizeOf<ColorVertex>() == 16 && Marshal.OffsetOf<ColorVertex>(nameof(ColorVertex.Color)).ToInt32() == 12, "Colored vertex ABI changed");
        var packed = new Color(0x78563412u);
        Require(packed.R == 0x12 && packed.G == 0x34 && packed.B == 0x56 && packed.A == 0x78, "RGBA byte order changed");
        packed.G = 255;
        Require(packed.PackedValue == 0x7856ff12u, "Packed color mutation changed");
        foreach (float channel in new[] { -1f, 0f, 0.001f, 0.1f, 0.5f, 0.9f, 1f, 2f })
        {
            var native = new Xna.Color(channel, channel, channel, channel);
            var radiant = new Color(channel, channel, channel, channel);
            Require(radiant.PackedValue == native.PackedValue, $"Normalized color quantization: {channel}");
            Require(radiant.ToVector4() == native.ToVector4().ToNumerics(), "Normalized color readback changed");
        }
        var bounds = new Rectangle(-3, 5, 7, 9);
        var nativeBounds = new Xna.Rectangle(-3, 5, 7, 9);
        for (int y = 3; y <= 16; y++) for (int x = -5; x <= 6; x++)
            Require(bounds.Contains(x, y) == nativeBounds.Contains(x, y), "Rectangle hit testing changed");
        Require(bounds.Center == new Point(0, 9) && !bounds.Intersects(new Rectangle(4, 5, 2, 2)), "Rectangle edge semantics changed");
        Require(!bounds.Contains(new Vector2(float.NaN, 6)), "NaN point is contained");

        // Cross-check row-vector order, handedness, clipping and matrix upload conversion.
        var world = Matrix.CreateScale(2, 3, -4) * Matrix.CreateFromYawPitchRoll(0.4f, -0.2f, 0.7f) * Matrix.CreateTranslation(10, -2, 3);
        var nativeWorld = Xna.Matrix.CreateScale(2, 3, -4) * Xna.Matrix.CreateFromYawPitchRoll(0.4f, -0.2f, 0.7f) * Xna.Matrix.CreateTranslation(10, -2, 3);
        Equal(world, nativeWorld.ToNumerics(), "World transform conventions");
        var eye = new Vector3(3, 4, 8);
        var view = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        Equal(view, Xna.Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY).ToNumerics(), "View handedness");
        var projection = Matrix.CreatePerspectiveFieldOfView(0.9f, 1.7f, 0.1f, 100);
        Equal(projection, Xna.Matrix.CreatePerspectiveFieldOfView(0.9f, 1.7f, 0.1f, 100).ToNumerics(), "Perspective depth range");
        Equal(Matrix.CreateOrthographicOffCenter(-3, 5, -2, 6, 0.1f, 50), Xna.Matrix.CreateOrthographicOffCenter(-3, 5, -2, 6, 0.1f, 50).ToNumerics(), "Orthographic conventions");
        Equal(MatrixMath.Invert(world), Xna.Matrix.Invert(nativeWorld).ToNumerics(), "Matrix inversion");
        // Large orthographic views exposed a one-ULP SIMD inverse difference at triangle edges.
        var screen = Matrix.CreateTranslation(0, 0, -100) * Matrix.CreateOrthographicOffCenter(0, 1200, 750, 0, 0.1f, 200);
        Require(MatrixMath.Invert(screen) == Xna.Matrix.Invert(screen).ToNumerics(), "Orthographic ray origin rounding changed");
        Xna.Matrix uploaded = world;
        Equal(world, uploaded.ToNumerics(), "Shader upload transposes or changes the matrix");
        var near = Vector4.Transform(new Vector4(0, 0, -0.1f, 1), projection);
        var far = Vector4.Transform(new Vector4(0, 0, -100, 1), projection);
        Require(Math.Abs(near.Z / near.W) < 0.00001f && Math.Abs(far.Z / far.W - 1) < 0.00001f, "Projection does not map near/far to 0/1");
        try { MatrixMath.Invert(default); throw new Exception("Singular matrix accepted"); } catch (ArgumentException) { }
        Console.WriteLine("PASS: public math API uses System.Numerics/Radiant, RGBA packing/quantization, rectangle hit testing, GPU/mesh ABI, transform order, camera handedness, clipping and inverse matrices.");
    }
    private static void Equal(Matrix A, Matrix B, string Message)
    {
        var a = MemoryMarshal.Cast<Matrix, float>(MemoryMarshal.CreateReadOnlySpan(ref A, 1));
        var b = MemoryMarshal.Cast<Matrix, float>(MemoryMarshal.CreateReadOnlySpan(ref B, 1));
        for (int i = 0; i < a.Length; i++) Require(Math.Abs(a[i] - b[i]) < 0.00001f, Message + $" at element {i}: {a[i]}/{b[i]}");
    }
    private static void Require(bool Condition, string Message) { if (!Condition) throw new Exception(Message); }
}
