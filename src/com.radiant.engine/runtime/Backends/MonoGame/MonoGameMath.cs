using System;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using Xna = Microsoft.Xna.Framework;

namespace com.radiant.engine.runtime;

/// <summary>Converts array parameters at the MonoGame boundary, retaining buffers to avoid per-frame allocations.</summary>
internal static class MonoGameMath
{
    private sealed class Buffer { public Array Values; }
    private static readonly ConditionalWeakTable<EffectParameter, Buffer> Buffers = new();
    private static T[] GetBuffer<T>(EffectParameter Parameter, int Length)
    {
        var buffer = Buffers.GetOrCreateValue(Parameter);
        if (buffer.Values is not T[] values || values.Length != Length)
            buffer.Values = values = new T[Length];
        return values;
    }
    public static void SetValue(EffectParameter Parameter, Vector2[] Values)
    {
        if (Parameter == null) return;
        ArgumentNullException.ThrowIfNull(Values);
        var buffer = GetBuffer<Xna.Vector2>(Parameter, Values.Length);
        for (int i = 0; i < Values.Length; i++) buffer[i] = Values[i];
        Parameter.SetValue(buffer);
    }
    public static void SetValue(EffectParameter Parameter, Vector3[] Values)
    {
        if (Parameter == null) return;
        ArgumentNullException.ThrowIfNull(Values);
        var buffer = GetBuffer<Xna.Vector3>(Parameter, Values.Length);
        for (int i = 0; i < Values.Length; i++) buffer[i] = Values[i];
        Parameter.SetValue(buffer);
    }
    public static void SetValue(EffectParameter Parameter, Vector4[] Values)
    {
        if (Parameter == null) return;
        ArgumentNullException.ThrowIfNull(Values);
        var buffer = GetBuffer<Xna.Vector4>(Parameter, Values.Length);
        for (int i = 0; i < Values.Length; i++) buffer[i] = Values[i];
        Parameter.SetValue(buffer);
    }
    public static void SetValue(EffectParameter Parameter, Matrix[] Values)
    {
        if (Parameter == null) return;
        ArgumentNullException.ThrowIfNull(Values);
        var buffer = GetBuffer<Xna.Matrix>(Parameter, Values.Length);
        for (int i = 0; i < Values.Length; i++) buffer[i] = Values[i];
        Parameter.SetValue(buffer);
    }
}
