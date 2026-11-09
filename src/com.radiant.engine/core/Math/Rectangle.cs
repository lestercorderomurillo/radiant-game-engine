using System;
using System.Runtime.InteropServices;
namespace com.radiant.engine.math;

/// <summary>Integer bounds. Point containment excludes the right and bottom edges.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Rectangle : IEquatable<Rectangle>
{
    public int X, Y, Width, Height;
    public Rectangle(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
    public Rectangle(Point location, Point size) : this(location.X, location.Y, size.X, size.Y) { }
    public static Rectangle Empty => default;
    public readonly int Left => X;
    public readonly int Top => Y;
    public readonly int Right => X + Width;
    public readonly int Bottom => Y + Height;
    public readonly Point Center => new Point(X + Width / 2, Y + Height / 2);
    public Point Location { readonly get => new Point(X, Y); set { X = value.X; Y = value.Y; } }
    public Point Size { readonly get => new Point(Width, Height); set { Width = value.X; Height = value.Y; } }
    public readonly bool IsEmpty => X == 0 && Y == 0 && Width == 0 && Height == 0;
    public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public readonly bool Contains(Vector2 point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    public readonly bool Contains(Point point) => Contains(point.X, point.Y);
    public readonly bool Contains(Rectangle other) => other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;
    public readonly bool Intersects(Rectangle other) => other.Left < Right && Left < other.Right && other.Top < Bottom && Top < other.Bottom;
    public readonly bool Equals(Rectangle other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override readonly bool Equals(object obj) => obj is Rectangle other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(Rectangle a, Rectangle b) => a.Equals(b);
    public static bool operator !=(Rectangle a, Rectangle b) => !a.Equals(b);
    public override readonly string ToString() => $"{{X:{X} Y:{Y} Width:{Width} Height:{Height}}}";
}
