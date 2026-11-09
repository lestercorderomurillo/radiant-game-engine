#include "TraceScene.fxh"
float4x4 InverseViewProjection;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };

PSIn VS(VSIn input) { PSIn o; o.Position = float4(input.Position, 1); o.UV = input.UV; return o; }
int SurfaceChannel;
float4x4 SurfaceView;
float SurfaceFar;

float4 SurfacePS(PSIn input) : SV_Target
{
    float4 nearPoint = mul(float4(input.UV.x * 2 - 1, 1 - input.UV.y * 2, 0, 1), InverseViewProjection);
    float4 farPoint = mul(float4(input.UV.x * 2 - 1, 1 - input.UV.y * 2, 1, 1), InverseViewProjection);
    nearPoint /= nearPoint.w; farPoint /= farPoint.w;
    float3 direction = normalize(farPoint.xyz - nearPoint.xyz);
    float distance; float3 normal, albedo, emission;

    if (!Intersect(nearPoint.xyz, direction, length(farPoint.xyz - nearPoint.xyz), distance, normal, albedo, emission)) return 0;
    if (SurfaceChannel == 0) return float4(albedo, 1);
    if (SurfaceChannel == 1) return float4(normal * 0.5 + 0.5, 1);
    if (SurfaceChannel == 3) return float4(emission, 1);

    float depth = -mul(float4(nearPoint.xyz + direction * distance, 1), SurfaceView).z / SurfaceFar;
    return float4(depth.xxx, 1);
}

technique Surface { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 SurfacePS(); } }
