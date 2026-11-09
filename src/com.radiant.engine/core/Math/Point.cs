using System;
using System.Runtime.InteropServices;
namespace com.radiant.engine.math;

/// <summary>An integer coordinate or size, independent of the graphics backend.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Point : IEquatable<Point>
{
    public int X, Y;
    public Point(int x, int y) { X = x; Y = y; }
    public static Point Zero => default;
    public readonly Vector2 ToVector2() => new Vector2(X, Y);
    public readonly bool Equals(Point other) => X == other.X && Y == other.Y;
    public override readonly bool Equals(object obj) => obj is Point other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(Point a, Point b) => a.Equals(b);
    public static bool operator !=(Point a, Point b) => !a.Equals(b);
    public override readonly string ToString() => $"{{X:{X} Y:{Y}}}";
}
