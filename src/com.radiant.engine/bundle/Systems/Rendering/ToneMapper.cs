using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>Tone curves used to map HDR radiance for display.</summary>
public enum ToneMapOperator { None, Reinhard, ACES, ACESFitted, AgX, Filmic, Uchimura, PBRNeutral }
/// <summary>Output transfer functions for linear or display-encoded color.</summary>
public enum ColorTransfer { Linear, SRGB, Gamma22 }

/// <summary>Dimension-independent exposure, tone mapping and output transfer. Does not present or own editor UI.</summary>
[SystemTag("Rendering", "Color")]
public class ToneMapper : ImageSystem
{
    /// <summary>Tone curve applied after exposure and before the output transfer function.</summary>
    public ToneMapOperator Operator = ToneMapOperator.ACES;
    public float Exposure = 1;
    public ColorTransfer Transfer = ColorTransfer.Linear;

    private bool Identity => Operator == ToneMapOperator.None && Exposure == 1 && Transfer == ColorTransfer.Linear;

    /// <summary>Returns the stage's current output image, or its input when processing is bypassed.</summary>
    public override Texture2D GetOutput() => !Enabled || Identity ? Input?.Invoke() : base.GetOutput();

    /// <inheritdoc/>
    public override void Render()
    {
        var input = Input?.Invoke();

        if (input == null || Identity) { if (Output != null) { Renderer.SetTarget(null); Output.Dispose(); Output = null; } return; }

        EnsureOutput(input, Operator == ToneMapOperator.None && Transfer == ColorTransfer.Linear ? SurfaceFormat.HalfVector4 : SurfaceFormat.Color);
        Renderer.Reset().SetShader("ColorManagement").SetTechnique("Color").Configure(SamplerState.PointClamp).SetTarget(Output).SetParameter("InputTexture", input).SetParameter("Mapper", (int)Operator).SetParameter("Exposure", Exposure).SetParameter("Transfer", (int)Transfer).Draw().Commit().SetTarget(null);
    }
}
