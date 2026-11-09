// Tomasi/Manduchi 1998 bilateral filtering; Dammertz et al. 2010 edge-avoiding a-trous.
// Engine adaptation: depth/albedo guides, diffuse demodulation and optional emission exclusion.
Texture2D Source : register(t0);
Texture2D Albedo : register(t1);
Texture2D Normals : register(t2);
Texture2D Depth : register(t3);
Texture2D Emission : register(t4);
Texture2D Filtered : register(t5);
SamplerState PointSampler : register(s0);
float2 TexelSize;
int StepWidth;
float ColorSigma;
float DepthSigma;
float NormalPower;
float AlbedoSigma;
bool HasEmission;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };

PSIn VS(VSIn input) { PSIn output; output.Position = float4(input.Position, 1); output.UV = input.UV; return output; }

float3 SurfaceColor(float2 uv) { float4 a = Albedo.SampleLevel(PointSampler, uv, 0); return a.rgb / max(a.a, 1e-6); }

float3 Illumination(float4 pixel, float3 albedo) { return pixel.rgb / max(pixel.a, 1e-6) / max(albedo, 0.02); }

float Kernel(int x) { return abs(x) == 2 ? 1.0 / 16 : abs(x) == 1 ? 1.0 / 4 : 3.0 / 8; }

float4 Filter(PSIn input, bool wavelet) : SV_Target
{
    float2 uv = input.UV;
    float4 center = Source.SampleLevel(PointSampler, uv, 0);
    float4 guide = Normals.SampleLevel(PointSampler, uv, 0);
    float centerDepth = Depth.SampleLevel(PointSampler, uv, 0).r;

    // The smaller one-sided difference estimates slope without treating a depth discontinuity as a slope.
    float depthSlope = min(abs(Depth.SampleLevel(PointSampler, uv - float2(TexelSize.x, 0), 0).r - centerDepth), abs(Depth.SampleLevel(PointSampler, uv + float2(TexelSize.x, 0), 0).r - centerDepth));
    depthSlope += min(abs(Depth.SampleLevel(PointSampler, uv - float2(0, TexelSize.y), 0).r - centerDepth), abs(Depth.SampleLevel(PointSampler, uv + float2(0, TexelSize.y), 0).r - centerDepth));

    if (center.a <= 0 || guide.a <= 0) return center;
    if (HasEmission && any(Emission.SampleLevel(PointSampler, uv, 0).rgb > 0)) return center;

    float3 normal = normalize(guide.rgb / max(guide.a, 1e-6) * 2 - 1);
    float3 albedo = SurfaceColor(uv);
    float3 illumination = Illumination(center, albedo);
    float colorScale = ColorSigma * max(1, dot(illumination, float3(0.2126, 0.7152, 0.0722)));
    float3 result = 0; float total = 0;
    [loop] for (int y = -2; y <= 2; y++) [loop] for (int x = -2; x <= 2; x++)
    {
        float2 offset = float2(x, y) * StepWidth;
        float2 sampleUV = uv + offset * TexelSize;

        if (any(sampleUV < 0) || any(sampleUV >= 1)) continue;

        float4 sampleNormal = Normals.SampleLevel(PointSampler, sampleUV, 0);

        if (sampleNormal.a <= 0) continue;
        if (HasEmission && any(Emission.SampleLevel(PointSampler, sampleUV, 0).rgb > 0)) continue;

        float4 pixel = Source.SampleLevel(PointSampler, sampleUV, 0);

        if (abs(pixel.a - center.a) > 0.01) continue;

        float3 sampleAlbedo = SurfaceColor(sampleUV);
        float3 sampleIllumination = Illumination(pixel, sampleAlbedo);
        float3 deltaAlbedo = sampleAlbedo - albedo;
        float3 deltaColor = sampleIllumination - illumination;
        float sampleDepth = Depth.SampleLevel(PointSampler, sampleUV, 0).r;
        float depthScale = max(DepthSigma * max(centerDepth, 1e-3), depthSlope * length(offset) * 1.5 + 1e-6);
        float depthDelta = (sampleDepth - centerDepth) / depthScale;
        float normalWeight = pow(saturate(dot(normal, normalize(sampleNormal.rgb / max(sampleNormal.a, 1e-6) * 2 - 1))), NormalPower);
        float weight = wavelet ? Kernel(x) * Kernel(y) : exp(-dot(float2(x, y), float2(x, y)) / 4.5);
        weight *= normalWeight * exp(-depthDelta * depthDelta * 0.5 - dot(deltaAlbedo, deltaAlbedo) / (2 * AlbedoSigma * AlbedoSigma) - dot(deltaColor, deltaColor) / (2 * colorScale * colorScale));
        result += sampleIllumination * weight; total += weight;
    }

    return total > 1e-8 ? float4(result / total * max(albedo, 0.02) * center.a, center.a) : center;
}

float4 BilateralPS(PSIn input) : SV_Target { return Filter(input, false); }

float4 AtrousPS(PSIn input) : SV_Target { return Filter(input, true); }
technique Bilateral { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 BilateralPS(); } }
technique Atrous { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 AtrousPS(); } }

float4 NeuralCompositePS(PSIn input) : SV_Target
{
    float4 current = Source.SampleLevel(PointSampler, input.UV, 0);

    if (current.a <= 0 || Normals.SampleLevel(PointSampler, input.UV, 0).a <= 0 || (HasEmission && any(Emission.SampleLevel(PointSampler, input.UV, 0).rgb > 0))) return current;

    float4 filtered = Filtered.SampleLevel(PointSampler, input.UV, 0);
    return float4(filtered.rgb / max(filtered.a, 1e-6) * current.a, current.a);
}

technique NeuralComposite { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 NeuralCompositePS(); } }
