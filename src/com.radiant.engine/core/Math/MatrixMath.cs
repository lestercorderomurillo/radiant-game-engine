using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace com.radiant.engine.math;

/// <summary>Backend-independent matrix operations using row-vector transforms (scale * rotation * translation).</summary>
public static class MatrixMath
{
    /// <summary>
    /// Inverts a matrix using double intermediates with explicit rounding of minors/cofactors.
    /// This keeps primary rays stable at triangle boundaries across the math backend migration.
    /// Throws for singular matrices or inverses that cannot be represented as finite floats.
    /// </summary>
    public static Matrix4x4 Invert(Matrix4x4 Value)
    {
        var values = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref Value, 1));
        Span<float> cofactors = stackalloc float[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                cofactors[row * 4 + column] = Cofactor(values, row, column);

        double determinant = (double)values[0] * cofactors[0] + (double)values[1] * cofactors[1]
            + (double)values[2] * cofactors[2] + (double)values[3] * cofactors[3];
        if (determinant == 0 || !double.IsFinite(determinant))
            throw new ArgumentException("Cannot invert a singular or nonfinite matrix.", nameof(Value));
        float reciprocal = (float)(1.0 / determinant);
        var inverse = new Matrix4x4();
        var output = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateSpan(ref inverse, 1));
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                float element = cofactors[column * 4 + row] * reciprocal;
                if (!float.IsFinite(element)) throw new ArgumentException("Matrix inverse is not finite.", nameof(Value));
                output[row * 4 + column] = element;
            }
        return inverse;
    }

    private static float Cofactor(ReadOnlySpan<float> Values, int ExcludedRow, int ExcludedColumn)
    {
        Span<float> minor = stackalloc float[9];
        int index = 0;
        for (int row = 0; row < 4; row++)
            if (row != ExcludedRow)
                for (int column = 0; column < 4; column++)
                    if (column != ExcludedColumn) minor[index++] = Values[row * 4 + column];
        float a = (float)((double)minor[4] * minor[8] - (double)minor[5] * minor[7]);
        float b = (float)((double)minor[3] * minor[8] - (double)minor[5] * minor[6]);
        float c = (float)((double)minor[3] * minor[7] - (double)minor[4] * minor[6]);
        double determinant = (double)minor[0] * a - (double)minor[1] * b + (double)minor[2] * c;
        return (float)(((ExcludedRow + ExcludedColumn) & 1) == 0 ? determinant : -determinant);
    }
}
