// Diffuse engine adaptation of Schied et al. 2017. No specular lobe or object motion-vector reconstruction.
Texture2D Source : register(t0);
Texture2D Albedo : register(t1);
Texture2D Normals : register(t2);
Texture2D Depth : register(t3);
Texture2D Emission : register(t4);
Texture2D History : register(t5);
Texture2D Moments : register(t6);
Texture2D Guides : register(t7);
Texture2D Progressive : register(t8);
SamplerState PointSampler : register(s0);
float2 TexelSize;
float4x4 CurrentProjection, InverseViewProjection, PreviousViewProjection, PreviousView;
float FarPlane, PreviousFar, DepthSigma, NormalPower, AlbedoSigma, Strength;
bool HasHistory, HasEmission, FinalPass;
int StepWidth;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };
PSIn VS(VSIn input) { PSIn o; o.Position = float4(input.Position, 1); o.UV = input.UV; return o; }
float Luma(float3 color) { return dot(color, float3(0.2126, 0.7152, 0.0722)); }
float4 Read(Texture2D image, float2 uv) { return image.SampleLevel(PointSampler, uv, 0); }
float3 Diffuse(float2 uv) { float4 color = Read(Albedo, uv); return max(color.rgb / max(color.a, 1e-6), 0.02); }
float3 Normal(float2 uv) { float4 n = Read(Normals, uv); return normalize(n.rgb / max(n.a, 1e-6) * 2 - 1); }
float3 Illumination(float2 uv) { float4 color = Read(Source, uv); return color.rgb / max(color.a, 1e-6) / Diffuse(uv); }
bool Surface(float2 uv) { return Read(Normals, uv).a > 0 && Read(Source, uv).a > 0; }
bool Emissive(float2 uv) { return HasEmission && any(Read(Emission, uv).rgb > 0); }
float DepthSlope(float2 uv, float depth)
{
    float x = min(abs(Read(Depth, uv - float2(TexelSize.x, 0)).r - depth), abs(Read(Depth, uv + float2(TexelSize.x, 0)).r - depth));
    float y = min(abs(Read(Depth, uv - float2(0, TexelSize.y)).r - depth), abs(Read(Depth, uv + float2(0, TexelSize.y)).r - depth));
    return x + y;
}
float GeometryWeight(float2 center, float2 other, float3 normal, float depth, float slope)
{
    if (any(other < 0) || any(other >= 1) || !Surface(other) || Emissive(other)) return 0;
    if (abs(Read(Source, center).a - Read(Source, other).a) > 0.01) return 0;
    float distance = length((other - center) / TexelSize);
    float scale = max(DepthSigma * max(depth, 1e-3), slope * distance * 1.5 + 1e-6);
    float dz = (Read(Depth, other).r - depth) / scale;
    float3 da = Diffuse(center) - Diffuse(other);
    return pow(saturate(dot(normal, Normal(other))), NormalPower) * exp(-0.5 * dz * dz - dot(da, da) / (2 * AlbedoSigma * AlbedoSigma));
}
struct TemporalOutputs { float4 Color : SV_Target0; float4 Moments : SV_Target1; float4 Guide : SV_Target2; };
TemporalOutputs TemporalPS(PSIn input)
{
    float2 uv = input.UV;
    float depth = Read(Depth, uv).r;
    float3 current = Illumination(uv), normal = Surface(uv) ? Normal(uv) : float3(0, 0, 1);
    float luminance = Luma(current);
    TemporalOutputs output;
    output.Guide = float4(normal * 0.5 + 0.5, depth);
    float3 history = 0; float4 moments = 0; float total = 0;
    if (HasHistory && depth > 0 && Surface(uv) && !Emissive(uv))
    {
        float4 projected = mul(float4(0, 0, -depth * FarPlane, 1), CurrentProjection);
        float4 world = mul(float4(uv.x * 2 - 1, 1 - uv.y * 2, projected.z / projected.w, 1), InverseViewProjection); world /= world.w;
        float4 clip = mul(world, PreviousViewProjection);
        float2 oldUV = clip.xy / max(clip.w, 1e-6) * float2(0.5, -0.5) + 0.5;
        float expectedDepth = -mul(world, PreviousView).z / max(PreviousFar, 1e-6);
        float2 oldPixel = oldUV / TexelSize - 0.5, first = floor(oldPixel), fraction = frac(oldPixel);
        [unroll] for (int y = 0; y < 2; y++) [unroll] for (int x = 0; x < 2; x++)
        {
            float2 tap = (first + float2(x, y) + 0.5) * TexelSize;
            if (clip.w <= 0 || any(tap < 0) || any(tap >= 1)) continue;
            float4 guide = Read(Guides, tap), m = Read(Moments, tap);
            if (guide.a <= 0 || m.z <= 0 || abs(guide.a - expectedDepth) > max(0.0005, abs(expectedDepth) * DepthSigma) || dot(normal, normalize(guide.rgb * 2 - 1)) < 0.95) continue;
            float weight = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
            history += Read(History, tap).rgb * weight; moments += m * weight; total += weight;
        }
    }
    float count = total > 1e-6 ? min(moments.z / total + 1, 32) : 1;
    float alpha = max(0.2, 1 / count);
    float2 currentMoments = float2(luminance, luminance * luminance);
    float2 integratedMoments = total > 1e-6 ? lerp(moments.xy / total, currentMoments, alpha) : currentMoments;
    float3 integrated = total > 1e-6 ? lerp(history / total, current, alpha) : current;
    float variance = max(integratedMoments.y - integratedMoments.x * integratedMoments.x, 0);
    if (count < 4 && Surface(uv) && !Emissive(uv))
    {
        float2 localMoments = 0; float weights = 0; float slope = DepthSlope(uv, depth);
        [loop] for (int y = -3; y <= 3; y++) [loop] for (int x = -3; x <= 3; x++)
        {
            float2 other = uv + float2(x, y) * TexelSize;
            float weight = GeometryWeight(uv, other, normal, depth, slope);
            if (weight <= 1e-8) continue;
            float l = Luma(Illumination(other)); localMoments += float2(l, l * l) * weight; weights += weight;
        }
        localMoments /= max(weights, 1e-6);
        variance = max(variance, max(localMoments.y - localMoments.x * localMoments.x, 0)) * (4 / count);
    }
    output.Color = float4(integrated, variance);
    output.Moments = float4(integratedMoments, Surface(uv) ? count : 0, 0);
    return output;
}
float Kernel(int i) { return abs(i) == 2 ? 1.0 / 16 : abs(i) == 1 ? 1.0 / 4 : 3.0 / 8; }
float4 WaveletPS(PSIn input) : SV_Target
{
    float2 uv = input.UV; float4 center = Read(History, uv);
    if (!Surface(uv) || Emissive(uv)) return FinalPass ? Read(Progressive, uv) : center;
    float3 normal = Normal(uv); float depth = Read(Depth, uv).r, slope = DepthSlope(uv, depth);
    // SVGF prefilters variance with a 3x3 Gaussian to avoid zero-variance islands rejecting rare light samples.
    float localVariance = 0, varianceWeights = 0;
    [unroll] for (int vy = -1; vy <= 1; vy++) [unroll] for (int vx = -1; vx <= 1; vx++)
    {
        float2 tap = uv + float2(vx, vy) * TexelSize;
        float weight = GeometryWeight(uv, tap, normal, depth, slope) * (vx == 0 ? 2 : 1) * (vy == 0 ? 2 : 1);
        localVariance += Read(History, tap).a * weight; varianceWeights += weight;
    }
    float centerLuma = Luma(center.rgb), colorScale = max(Strength * 2 * sqrt(max(localVariance / max(varianceWeights, 1e-6), 0)), 1e-4);
    float3 color = 0; float variance = 0, total = 0;
    [loop] for (int y = -2; y <= 2; y++) [loop] for (int x = -2; x <= 2; x++)
    {
        float2 other = uv + float2(x, y) * StepWidth * TexelSize;
        float weight = GeometryWeight(uv, other, normal, depth, slope) * Kernel(x) * Kernel(y);
        if (weight <= 1e-8) continue;
        float4 sampleColor = Read(History, other);
        weight *= exp(-abs(Luma(sampleColor.rgb) - centerLuma) / colorScale);
        color += sampleColor.rgb * weight; variance += sampleColor.a * weight * weight; total += weight;
    }
    float4 result = total > 1e-8 ? float4(color / total, max(variance / (total * total), 0)) : center;
    return FinalPass ? float4(result.rgb * Diffuse(uv) * Read(Source, uv).a, Read(Source, uv).a) : result;
}
float4 CompositePS(PSIn input) : SV_Target
{
    float2 uv = input.UV;
    return Surface(uv) && !Emissive(uv) ? float4(Read(History, uv).rgb * Diffuse(uv) * Read(Source, uv).a, Read(Source, uv).a) : Read(Progressive, uv);
}
technique Temporal { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 TemporalPS(); } }
technique Wavelet { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 WaveletPS(); } }
technique Composite { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 CompositePS(); } }
