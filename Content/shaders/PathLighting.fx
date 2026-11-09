Texture2D Source : register(t0);
Texture2D Albedo : register(t1);
Texture2D Normals : register(t2);
Texture2D Depth : register(t3);
Texture2D Emission : register(t4);
Texture2D FullAlbedo : register(t5);
Texture2D FullNormals : register(t6);
Texture2D FullDepth : register(t7);
Texture2D FullEmission : register(t8);
SamplerState PointSampler : register(s0);
float2 LightingSize;
float3 Environment;
bool HasCamera;
bool NativeResolution;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };

PSIn VS(VSIn input) { PSIn output; output.Position = float4(input.Position, 1); output.UV = input.UV; return output; }

float4 PS(PSIn input) : SV_Target
{
    if (!HasCamera) return 0;
    if (NativeResolution) return Source.SampleLevel(PointSampler, input.UV, 0);

    float4 albedo = FullAlbedo.SampleLevel(PointSampler, input.UV, 0);

    if (albedo.a <= 0) return float4(Environment, 1);

    float3 emission = FullEmission.SampleLevel(PointSampler, input.UV, 0).rgb;
    float3 normal = normalize(FullNormals.SampleLevel(PointSampler, input.UV, 0).rgb * 2 - 1);
    float depth = FullDepth.SampleLevel(PointSampler, input.UV, 0).r;
    float2 center = floor(input.UV * LightingSize);
    float3 illumination = 0;
    float total = 0;

    // Reconstruct irradiance, rather than enlarging already-colored radiance.
    [loop] for (int y = -2; y <= 2; y++) [loop] for (int x = -2; x <= 2; x++)
    {
        float2 sampleUV = (center + float2(x, y) + 0.5) / LightingSize;

        if (any(sampleUV < 0) || any(sampleUV >= 1)) continue;

        float4 guide = Normals.SampleLevel(PointSampler, sampleUV, 0);

        if (guide.a <= 0) continue;

        float3 sampleNormal = normalize(guide.rgb * 2 - 1);
        float normalWeight = pow(saturate(dot(normal, sampleNormal)), 64);
        float sampleDepth = Depth.SampleLevel(PointSampler, sampleUV, 0).r;
        float3 sampleAlbedo = Albedo.SampleLevel(PointSampler, sampleUV, 0).rgb;

        // Reject emissive neighbors: their radiance does not estimate incident diffuse light.
        if (any(Emission.SampleLevel(PointSampler, sampleUV, 0).rgb > 0)) continue;

        float2 offset = (sampleUV - input.UV) * LightingSize;
        float depthDelta = (sampleDepth - depth) / max(0.02 * depth, 0.0001);
        float weight = normalWeight * exp(-0.5 * depthDelta * depthDelta - dot(offset, offset) / 2);
        float3 radiance = Source.SampleLevel(PointSampler, sampleUV, 0).rgb;
        illumination += radiance / max(sampleAlbedo, 0.02) * weight;
        total += weight;
    }

    // A subpixel surface can have no low-resolution sample; use ambient light until it has one.
    return float4(albedo.rgb * (total > 1e-8 ? illumination / total : Environment) + emission, 1);
}

technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }
