/* HRC_MergingCones.fx
   Merges radiance using the ray intervals' RGB transmittance for stained glass.
   Far cones only need radiance; merged transmittance is never consumed. */

Texture2D VraysRadiance : register(t0);
Texture2D VraysTransmittance : register(t1);
Texture2D PrevRadiance : register(t2);

SamplerState Sampler0 : register(s0);

float2 VraysSize;
float2 PrevSize;
float2 CascadeSize;
float2 CascadeIndex;
float ProbeScale;

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

void GetVolumeVrays(float2 probe, float index, float interval, float lookupWidth,
                    out float3 rad, out float3 trans)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / VraysSize;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    rad = lerp(VraysRadiance.Sample(Sampler0, samplePos).rgb, float3(0, 0, 0), weight);
    trans = lerp(VraysTransmittance.Sample(Sampler0, samplePos).rgb, float3(1, 1, 1), weight);
}

float3 GetVolumePrev(float2 probe, float index, float interval, float lookupWidth)
{
    float2 samplePos = float2(floor(probe.x / interval) * lookupWidth, probe.y / ProbeScale) + float2(0.5, 0.0);
    samplePos = float2(samplePos.x + index, samplePos.y) / PrevSize;

    float2 floorPos = floor(samplePos);
    float weight = (floorPos.x != 0.0 || floorPos.y != 0.0) ? 1.0 : 0.0;

    return lerp(PrevRadiance.Sample(Sampler0, samplePos).rgb, float3(0, 0, 0), weight);
}

float3 MergeCone(float2 probe, float plane, float intrv, float vrays, float index, float side)
{
    float coneI = index * 2.0 + side;
    float vrayI = index + side;
    float2 limit = float2(intrv, -intrv);
    float align = 2.0 - fmod(plane, 2.0);

    float2 merge = probe + align * (limit + float2(0.0, vrayI * 2.0));

    float2 vrayLL = (limit * 2.0) + float2(0.0, coneI * 2.0);
    float2 vrayRR = (limit * 2.0) + float2(0.0, (coneI + 1.0) * 2.0);
    // This is already the child cone's angular width. The children partition
    // the quadrant; global normalization belongs in HRC_FluenceSum.
    float coneW = atan(vrayRR.y / vrayRR.x) - atan(vrayLL.y / vrayLL.x);

    float3 vrayRad, vrayTrans;
    GetVolumeVrays(probe, vrayI, intrv, vrays, vrayRad, vrayTrans);
    float3 coneFarRad = GetVolumePrev(merge, coneI, 1.0, 1.0);

    if (fmod(plane, 2.0) < 0.5)
    {
        float2 probeFar = probe + (limit + float2(0.0, vrayI * 2.0));
        float2 probeNear = probe;

        float3 vrayExtRad, vrayExtTrans;
        GetVolumeVrays(probeFar, vrayI, intrv, vrays, vrayExtRad, vrayExtTrans);
        float3 coneNearRad = GetVolumePrev(probeNear, coneI, 1.0, 1.0);

        float3 extRad = vrayRad + vrayExtRad * vrayTrans;
        float3 extTrans = vrayTrans * vrayExtTrans;

        float3 weightedRad = extRad * coneW;
        float3 resultRad = weightedRad + coneFarRad * extTrans;

        return lerp(resultRad, coneNearRad, 0.5);
    }
    else
    {
        return (vrayRad * coneW) + (coneFarRad * vrayTrans);
    }
}

float4 MainPS(PixelShaderInput input) : SV_Target0
{
    float2 texel = input.UV * CascadeSize;
    float intrv = exp2(CascadeIndex.x);
    float vrays = intrv + 1.0;
    float plane = floor(texel.x / intrv);
    float index = floor(texel.x - (plane * intrv));
    float2 probe = float2(plane * intrv, texel.y * ProbeScale) + float2(0.5, 0.0);

    float3 radL = MergeCone(probe, plane, intrv, vrays, index, 0.0);
    float3 radR = MergeCone(probe, plane, intrv, vrays, index, 1.0);
    return float4(radL + radR, 1.0);
}

technique GenerateOutputTexture
{
    pass P0
    {
        VertexShader = compile vs_6_0 MainVS();
        PixelShader = compile ps_6_0 MainPS();
    }
}
