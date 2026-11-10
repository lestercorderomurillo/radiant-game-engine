/* RC_Irradiance.fx
   Integrates merged cascade 0 cones into a small per-probe irradiance map, one texel per normal direction. */

#include "../TraceScene.fxh"
#include "RC_Common.fxh"

Texture2D Cascade : register(t0);
SamplerState CascadeSampler : register(s0);

float2 CascadeAtlasSize;
int CascadeTilesPerRow;
int Theta;
int TilesPerRow;
float2 TileSize;
int GridSize;

struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };

PSIn VS(VSIn input) { PSIn output; output.Position = float4(input.Position, 1); output.UV = input.UV; return output; }

float4 PS(PSIn input) : SV_Target
{
    int2 texel = (int2)input.Position.xy;
    int2 tileSize = (int2)TileSize;
    int2 tile = texel / tileSize;
    int2 local = texel % tileSize;
    int probeIndex = tile.y * TilesPerRow + tile.x;

    if (probeIndex >= GridSize * GridSize * GridSize) return 0;

    float3 normal = IrradianceNormal(local, tileSize);
    int2 cascadeTile = int2(2 * Theta, Theta);
    int2 origin = TileOrigin(probeIndex, CascadeTilesPerRow, cascadeTile);
    float3 total = 0;
    float weightSum = 0;

    // Equal-area directions share one solid angle; normalizing by the cosine sum keeps a uniform field exact.
    [loop] for (int row = 0; row < cascadeTile.y; row++)
    [loop] for (int column = 0; column < cascadeTile.x; column++)
    {
        float3 direction = DecodeDirection((float2(column, row) + 0.5) / float2(cascadeTile));
        float cosine = dot(direction, normal);

        if (cosine <= 0) continue;

        total += cosine * Cascade.SampleLevel(CascadeSampler, (origin + int2(column, row) + 0.5) / CascadeAtlasSize, 0).rgb;
        weightSum += cosine;
    }

    return float4(RC_PI * total / max(weightSum, 1e-6), 1);
}

technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }
