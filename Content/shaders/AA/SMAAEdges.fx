#include "SMAACommon.fxh"
Texture2D Source : register(t0);
float4 PS(PSIn input) : SV_Target
{
    float4 offsets[3] = { input.Offset0, input.Offset1, input.Offset2 };
    float4 center = Source.SampleLevel(LinearSampler, input.UV, 0);
    float4 left = Source.SampleLevel(LinearSampler, offsets[0].xy, 0);
    float4 top = Source.SampleLevel(LinearSampler, offsets[0].zw, 0);
    float2 coverage = step(SMAA_THRESHOLD, abs(center.aa - float2(left.a, top.a)));
    if (any(coverage > 0))
    {
        float3 dl = abs(center.rgb - left.rgb), dt = abs(center.rgb - top.rgb);
        float2 color = step(SMAA_THRESHOLD, float2(max(dl.r, max(dl.g, dl.b)), max(dt.r, max(dt.g, dt.b))));
        return float4(max(coverage, color), 0, 0);
    }
    return float4(SMAAColorEdgeDetectionPS(input.UV, offsets, Source), 0, 0);
}
technique Default { pass P0 { VertexShader = compile vs_6_0 EdgeVS(); PixelShader = compile ps_6_0 PS(); } }
