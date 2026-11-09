Texture2D InputTexture : register(t0);
SamplerState Sampler0 : register(s0);
int Mapper;
float Exposure;
int Transfer;

struct VertexShaderInput
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD0;
};

PixelShaderInput MainVS(VertexShaderInput input)
{
    PixelShaderInput output;
    output.Position = float4(input.Position, 1.0);
    output.UV = input.TexCoord;
    return output;
}


// ACES: Narkowicz fit ─────────────────────────────────────────────────────

float3 ACESNarkowicz(float3 x)
{
    return saturate((x * mad(2.51, x, 0.03)) / mad(x, mad(2.43, x, 0.59), 0.14));
}

float3 Map_ACES(float3 color)
{
    color = ACESNarkowicz(color);
    return color;
}

// ACES fitted: Hill RRT+ODT ────────────────────────────────────────────────────

static const float3x3 ACESInputMat = float3x3(
    0.59719, 0.35458, 0.04823,
    0.07600, 0.90834, 0.01566,
    0.02840, 0.13383, 0.83777
);

static const float3x3 ACESOutputMat = float3x3(
     1.60475, -0.53108, -0.07367,
    -0.10208,  1.10813, -0.00605,
    -0.00327, -0.07276,  1.07602
);

float3 RRTAndODTFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 Map_ACESFitted(float3 color)
{
    color = mul(ACESInputMat, color);
    color = RRTAndODTFit(color);
    color = mul(ACESOutputMat, color);
    color = saturate(color);
    return color;
}

// ── AgX: Magennis ──────────────────────────────────────────────────────────

static const float3x3 AgXInsetMatrix = float3x3(
    0.842479062253094,  0.0784335999999992, 0.0792237451477643,
    0.0423282422610123, 0.878468636469772,  0.0791661274605434,
    0.0423756549057051, 0.0784336,          0.879142973793104
);

static const float3x3 AgXOutsetMatrix = float3x3(
     1.19687900512017,  -0.0980208811401368, -0.0990297440797205,
    -0.0528968517574562, 1.15190312990417,   -0.0989611768448433,
    -0.0529716355144438, -0.0980434501171241, 1.15107367264116
);

float3 AgXDefaultContrastApprox(float3 x)
{
    // 6th-order polynomial approximation of the AgX contrast curve
    float3 x2 = x * x;
    float3 x4 = x2 * x2;
    return + 15.5     * x4 * x2
           - 40.14    * x4 * x
           + 31.96    * x4
           - 6.868    * x2 * x
           + 0.4298   * x2
           + 0.1191   * x
           - 0.00232;
}

float3 Map_AgX(float3 color)
{

    // AgX inset
    color = mul(AgXInsetMatrix, color);

    // Log2 encode: map [-10, +6.5] to [0, 1]
    color = max(color, 1e-10);
    color = log2(color);
    color = (color - (-10.0)) / (6.5 - (-10.0)); // [-10,6.5] → [0,1]
    color = saturate(color);

    // Apply contrast curve
    color = AgXDefaultContrastApprox(color);

    // AgX outset
    color = mul(AgXOutsetMatrix, color);
    color = saturate(color);

    return color;
}

// Filmic: Hable (Uncharted 2)

float3 HablePartial(float3 x)
{
    float A = 0.15; // Shoulder strength
    float B = 0.50; // Linear strength
    float C = 0.10; // Linear angle
    float D = 0.20; // Toe strength
    float E = 0.02; // Toe numerator
    float F = 0.30; // Toe denominator
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

float3 Map_Filmic(float3 color)
{
    float exposureBias = 2.0;
    float3 mapped = HablePartial(color * exposureBias);
    float3 whiteScale = 1.0 / HablePartial(11.2);
    color = mapped * whiteScale;
    return color;
}

// Reinhard

float3 Map_Reinhard(float3 color)
{
    color = color / (1.0 + color);
    return color;
}

// Uchimura (Gran Turismo)

float UchimuraCurve(float x, float P, float a, float m, float l, float c, float b)
{
    float l0 = ((P - m) * l) / a;
    float S0 = m + l0;
    float S1 = m + a * l0;
    float C2 = (a * P) / (P - S1);
    float CP = -C2 / P;

    float w0 = 1.0 - smoothstep(0.0, m, x);
    float w2 = step(m + l0, x);
    float w1 = 1.0 - w0 - w2;

    float T = m * pow(x / m, c) + b;
    float S = P - (P - S1) * exp(CP * (x - S0));
    float L = m + a * (x - m);

    return T * w0 + L * w1 + S * w2;
}

float3 Map_Uchimura(float3 color)
{
    float P = 1.0;   // Max brightness
    float a = 1.0;   // Contrast
    float m = 0.22;  // Linear section start
    float l = 0.4;   // Linear section length
    float c = 1.33;  // Black tightness curve
    float b = 0.0;   // Black offset
    color.r = UchimuraCurve(color.r, P, a, m, l, c, b);
    color.g = UchimuraCurve(color.g, P, a, m, l, c, b);
    color.b = UchimuraCurve(color.b, P, a, m, l, c, b);
    return color;
}

// PBR Neutral (Khronos)

float3 PBRNeutral(float3 color)
{
    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;

    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;

    float peak = max(color.r, max(color.g, color.b));

    if (peak < startCompression) return color;

    float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / peak;

    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return lerp(color, newPeak, g);
}

float3 Map_PBRNeutral(float3 color)
{
    color = PBRNeutral(color);
    return color;
}

// All renderers share the same color operators. Input/output use premultiplied alpha.
float3 Encode(float3 color)
{
    color = max(color, 0);

    if (Transfer == 2) return pow(color, 1.0 / 2.2);
    if (Transfer == 1) return lerp(1.055 * pow(color, 1.0 / 2.4) - 0.055, 12.92 * color, step(color, 0.0031308));

    return color;
}

float4 ColorPS(PixelShaderInput input) : SV_Target0
{
    float4 pixel = InputTexture.Sample(Sampler0, input.UV);

    if (pixel.a <= 0) return 0;

    float3 color = max(pixel.rgb / max(pixel.a, 1e-6) * Exposure, 0);
    if (Mapper == 1) color = Map_Reinhard(color);
    else if (Mapper == 2) color = Map_ACES(color);
    else if (Mapper == 3) color = Map_ACESFitted(color);
    else if (Mapper == 4) color = Map_AgX(color);
    else if (Mapper == 5) color = Map_Filmic(color);
    else if (Mapper == 6) color = Map_Uchimura(color);
    else if (Mapper == 7) color = Map_PBRNeutral(color);
    return float4(Encode(color) * pixel.a, pixel.a);
}

technique Color
{
    pass P0
    {
        VertexShader = compile vs_6_0 MainVS();
        PixelShader = compile ps_6_0 ColorPS();
    }
}
