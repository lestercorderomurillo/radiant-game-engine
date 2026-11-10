/* RC_Gather.fx
   Shades full-resolution primary hits with interpolated probe irradiance and writes linear depth. */

#include "../TraceScene.fxh"
#include "RC_Common.fxh"

Texture2D Irradiance : register(t1);
SamplerState IrradianceSampler : register(s1);

float4x4 InverseViewProjection;
float4x4 View;
float FarPlane;
float3 Environment;
bool HasCamera;
float2 AtlasSize;
int TilesPerRow;
float2 TileSize;
int GridSize;
float3 GridMin;
float Spacing;
float NormalBias;

struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };
struct Outputs { float4 Color : SV_Target0; float4 Depth : SV_Target1; };

PSIn VS(VSIn input) { PSIn output; output.Position = float4(input.Position, 1); output.UV = input.UV; return output; }

Outputs PS(PSIn input)
{
    Outputs output;
    output.Color = 0;
    output.Depth = 0;

    if (!HasCamera) return output;

    float4 nearPoint = mul(float4(input.UV.x * 2 - 1, 1 - input.UV.y * 2, 0, 1), InverseViewProjection);
    float4 farPoint = mul(float4(input.UV.x * 2 - 1, 1 - input.UV.y * 2, 1, 1), InverseViewProjection);
    nearPoint /= nearPoint.w;
    farPoint /= farPoint.w;
    float3 direction = normalize(farPoint.xyz - nearPoint.xyz);
    float distance;
    float3 normal, albedo, emission;

    if (!Intersect(nearPoint.xyz, direction, length(farPoint.xyz - nearPoint.xyz), distance, normal, albedo, emission))
    {
        output.Color = float4(Environment, 1);
        return output;
    }

    float3 position = nearPoint.xyz + direction * distance;
    float3 irradiance = SampleIrradiance(Irradiance, IrradianceSampler, AtlasSize, TilesPerRow, (int2)TileSize, GridSize, GridMin, Spacing, position, normal, NormalBias, true);
    output.Color = float4(albedo * irradiance / RC_PI + emission, 1);
    output.Depth = float4((-mul(float4(position, 1), View).z / FarPlane).xxx, 1);
    return output;
}

technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }
