using Xna = Microsoft.Xna.Framework;

namespace com.radiant.engine.runtime;

// Conversion belongs to the backend, never to the public value types.
internal static class MonoGamePrimitives
{
    internal static Xna.Color ToNative(this Color value) => new Xna.Color(value.PackedValue);
    internal static Xna.Rectangle ToNative(this Rectangle value) => new Xna.Rectangle(value.X, value.Y, value.Width, value.Height);
    internal static Xna.Rectangle? ToNative(this Rectangle? value) => value.HasValue ? value.Value.ToNative() : null;
    internal static Rectangle ToRadiant(this Xna.Rectangle value) => new Rectangle(value.X, value.Y, value.Width, value.Height);
}
