/* RC_Intervals.fx
   Traces one radiance interval per probe direction of a cascade and merges it with the
   trilinearly interpolated, directionally averaged cones of the next cascade. */

#include "../TraceScene.fxh"
#include "RC_Common.fxh"

Texture2D Upper : register(t1);
SamplerState UpperSampler : register(s1);
Texture2D Feedback : register(t2);
SamplerState FeedbackSampler : register(s2);

float2 AtlasSize;
int TilesPerRow;
int GridSize;
int Theta;
float Spacing;
float3 GridMin;
float IntervalStart;
float IntervalEnd;
bool LeakFix;
float3 Environment;

bool HasUpper;
float2 UpperAtlasSize;
int UpperTilesPerRow;
int UpperGridSize;
int UpperTheta;
float UpperSpacing;

bool HasFeedback;
float2 FeedbackAtlasSize;
int FeedbackTilesPerRow;
float2 FeedbackTileSize;
int FeedbackGridSize;
float FeedbackSpacing;
float NormalBias;

struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };

PSIn VS(VSIn input) { PSIn output; output.Position = float4(input.Position, 1); output.UV = input.UV; return output; }

// Average of the four child directions of one upper probe's cone.
float3 UpperCone(int3 cell, int2 local)
{
    int2 tileSize = int2(2 * UpperTheta, UpperTheta);
    float2 uv = (TileOrigin(ProbeIndex(cell, UpperGridSize), UpperTilesPerRow, tileSize) + float2(local) * 2.0 + 1.0) / UpperAtlasSize;
    return Upper.SampleLevel(UpperSampler, uv, 0).rgb;
}

// Emission and transmittance of one traced segment, reporting the hit for a shared bounce lookup.
float4 TraceInterval(float3 start, float3 direction, float maxDistance, bool toInfinity, inout float3 hitPosition, inout float3 hitNormal, out float3 hitAlbedo)
{
    float distance;
    float3 normal, albedo, emission;
    hitAlbedo = 0;

    if (Intersect(start, direction, toInfinity ? 1e30 : maxDistance, distance, normal, albedo, emission))
    {
        hitPosition = start + direction * distance;
        hitNormal = normal;
        hitAlbedo = albedo;
        return float4(emission, 0);
    }

    return toInfinity ? float4(Environment, 0) : float4(0, 0, 0, 1);
}

// Each segment uses its own hit's material, position and normal for bounce feedback.
float3 Bounce(float3 albedo, float3 hitPosition, float3 hitNormal)
{
    if (!HasFeedback || all(albedo <= 0)) return 0;

    return albedo * SampleIrradiance(Feedback, FeedbackSampler, FeedbackAtlasSize, FeedbackTilesPerRow, (int2)FeedbackTileSize, FeedbackGridSize,
        GridMin, FeedbackSpacing, hitPosition, hitNormal, NormalBias, true) / RC_PI;
}

float4 PS(PSIn input) : SV_Target
{
    int2 texel = (int2)input.Position.xy;
    int2 tileSize = int2(2 * Theta, Theta);
    int2 tile = texel / tileSize;
    int2 local = texel % tileSize;
    int probeIndex = tile.y * TilesPerRow + tile.x;

    if (probeIndex >= GridSize * GridSize * GridSize) return 0;

    float3 origin = ProbePosition(ProbeCell(probeIndex, GridSize), GridMin, Spacing);
    float3 direction = DecodeDirection((float2(local) + 0.5) / float2(tileSize));

    // Rays begin where the parent direction's segment ends, so chained intervals leave no gap at the hand-over.
    float3 parent = IntervalStart > 0 ? DecodeDirection((float2(local / 2) + 0.5) / float2(tileSize / 2)) : direction;
    float3 start = origin + parent * IntervalStart;
    float3 hitPosition = 0, hitNormal = 0, hitAlbedo;

    if (!HasUpper)
    {
        float4 interval = TraceInterval(start, direction, 0, true, hitPosition, hitNormal, hitAlbedo);
        return float4(interval.rgb + Bounce(hitAlbedo, hitPosition, hitNormal), interval.a);
    }

    float3 grid = (origin - GridMin) / UpperSpacing - 0.5;
    float3 base = floor(grid);
    float3 fraction = saturate(grid - base);
    float4 interval = LeakFix ? 0 : TraceInterval(start, direction, IntervalEnd - IntervalStart, false, hitPosition, hitNormal, hitAlbedo);
    if (!LeakFix) interval.rgb += Bounce(hitAlbedo, hitPosition, hitNormal);
    float4 total = 0;

    [loop] for (int corner = 0; corner < 8; corner++)
    {
        int3 offset = int3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
        int3 cell = clamp(int3(base) + offset, 0, UpperGridSize - 1);
        float3 trilinear = lerp(1.0 - fraction, fraction, float3(offset));
        float weight = trilinear.x * trilinear.y * trilinear.z;

        // Connect each upper probe with its own segment so a wall between the two probes blocks that probe's cone.
        if (LeakFix)
        {
            float3 segment = ProbePosition(cell, GridMin, UpperSpacing) + direction * IntervalEnd - start;
            float segmentLength = max(length(segment), 1e-5);
            interval = TraceInterval(start, segment / segmentLength, segmentLength, false, hitPosition, hitNormal, hitAlbedo);
            interval.rgb += Bounce(hitAlbedo, hitPosition, hitNormal);
        }

        total += weight * float4(interval.rgb + interval.a * UpperCone(cell, local), interval.a);
    }

    return total;
}

technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }
