#include "TraceScene.fxh"
Texture2D History : register(t1);
Texture2D ExternalSample : register(t2);
SamplerState ExternalSampler : register(s2);
SamplerState HistorySampler : register(s1);
int SampleIndex;
int SequenceIndex;
int MaxBounces;
float4x4 InverseViewProjection;
float3 CameraPosition;
bool Orthographic;
float3 Environment;
float2 OutputSize;
bool StablePrimary;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };

PSIn VS(VSIn input) { PSIn o; o.Position = float4(input.Position, 1); o.UV = input.UV; return o; }

uint Hash(uint v) { v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16); }

float Random(inout uint state) { state = Hash(state + 0x9e3779b9u); return (state >> 8) * (1.0 / 16777216.0); }

float3 Hemisphere(float3 n, inout uint state)
{
    float u = Random(state), angle = 6.28318530718 * Random(state);
    float3 tangent = normalize(cross(abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(0, 1, 0), n));
    return normalize(tangent * (sqrt(u) * cos(angle)) + cross(n, tangent) * (sqrt(u) * sin(angle)) + n * sqrt(1 - u));
}

float3 TraceRadiance(PSIn input)
{
    uint state = Hash((uint)input.Position.x + (uint)input.Position.y * (uint)OutputSize.x + (uint)SequenceIndex * 747796405u);
    float2 jitter = float2(Random(state), Random(state)) - 0.5;
    float2 uv = input.UV + (StablePrimary ? float2(0, 0) : jitter) / OutputSize;
    float4 nearPoint = mul(float4(uv.x * 2 - 1, 1 - uv.y * 2, 0, 1), InverseViewProjection);
    float4 farPoint = mul(float4(uv.x * 2 - 1, 1 - uv.y * 2, 1, 1), InverseViewProjection);
    nearPoint /= nearPoint.w; farPoint /= farPoint.w;
    float3 origin = nearPoint.xyz;
    float3 direction = normalize(farPoint.xyz - nearPoint.xyz);
    float primaryLimit = length(farPoint.xyz - nearPoint.xyz);
    float3 radiance = 0, throughput = 1;
    [loop] for (int bounce = 0; bounce < MaxBounces; bounce++)
    {
        float distance; float3 normal, albedo, emission;

        if (!Intersect(origin, direction, bounce == 0 ? primaryLimit : 1e30, distance, normal, albedo, emission)) { radiance += throughput * Environment; break; }

        radiance += throughput * emission;
        throughput *= albedo;
        origin += direction * distance + normal * 0.0005;
        direction = Hemisphere(normal, state);
        if (bounce >= 2)
        {
            float survival = clamp(max(throughput.r, max(throughput.g, throughput.b)), 0.05, 0.95);

            if (Random(state) >= survival) break;

            throughput /= survival;
        }
    }

    return radiance;
}

float4 Accumulate(PSIn input, float3 radiance)
{
    float3 previous = SampleIndex > 0 ? History.SampleLevel(HistorySampler, input.UV, 0).rgb : 0;
    return float4(lerp(previous, radiance, 1.0 / (SampleIndex + 1)), 1);
}

float4 PS(PSIn input) : SV_Target { return Accumulate(input, TraceRadiance(input)); }
struct TraceOutputs { float4 Accumulated : SV_Target0; float4 Sample : SV_Target1; };
TraceOutputs SamplePS(PSIn input)
{
    float3 radiance = TraceRadiance(input);
    TraceOutputs output; output.Accumulated = Accumulate(input, radiance); output.Sample = float4(radiance, 1); return output;
}

technique WithSample { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 SamplePS(); } }
technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }

float4 ExternalPS(PSIn input) : SV_Target { return Accumulate(input, ExternalSample.SampleLevel(ExternalSampler, input.UV, 0).rgb); }
technique External { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 ExternalPS(); } }
