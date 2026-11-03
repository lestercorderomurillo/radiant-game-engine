Texture2D Source : register(t0);
Texture2D Normals : register(t1);
SamplerState ImageSampler : register(s0);
float2 TexelSize;
float EdgeThreshold;
float3 LightDirection;
float3 LightColor;
float Ambient;
bool HasNormals;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };
PSIn VS(VSIn input) { PSIn o; o.Position = float4(input.Position, 1); o.UV = input.UV; return o; }
float4 RasterPS(PSIn input) : SV_Target
{
    float4 color = Source.Sample(ImageSampler, input.UV);
    float4 encodedNormal = Normals.Sample(ImageSampler, input.UV);
    float3 normal = HasNormals ? normalize(encodedNormal.xyz / max(encodedNormal.a, 1e-6) * 2 - 1) : float3(0, 0, 1);
    return float4(color.rgb * (Ambient + max(dot(normal, LightDirection), 0) * LightColor), color.a);
}
float Luma(float3 color) { return dot(color, float3(0.299, 0.587, 0.114)); }
float4 FXAAPS(PSIn input) : SV_Target
{
    float2 uv = input.UV;
    float4 center = Source.Sample(ImageSampler, uv);
    float4 nwSample = Source.Sample(ImageSampler, uv + float2(-1, -1) * TexelSize);
    float4 neSample = Source.Sample(ImageSampler, uv + float2(1, -1) * TexelSize);
    float4 swSample = Source.Sample(ImageSampler, uv + float2(-1, 1) * TexelSize);
    float4 seSample = Source.Sample(ImageSampler, uv + float2(1, 1) * TexelSize);
    float3 nw = nwSample.rgb, ne = neSample.rgb, sw = swSample.rgb, se = seSample.rgb;
    float lNW = Luma(nw), lNE = Luma(ne), lSW = Luma(sw), lSE = Luma(se), lM = Luma(center.rgb);
    float low = min(lM, min(min(lNW, lNE), min(lSW, lSE)));
    float high = max(lM, max(max(lNW, lNE), max(lSW, lSE)));
    float alphaLow = min(center.a, min(min(nwSample.a, neSample.a), min(swSample.a, seSample.a)));
    float alphaHigh = max(center.a, max(max(nwSample.a, neSample.a), max(swSample.a, seSample.a)));
    bool coverageEdge = alphaHigh - alphaLow > high - low;
    if (coverageEdge) { lNW = nwSample.a; lNE = neSample.a; lSW = swSample.a; lSE = seSample.a; lM = center.a; low = alphaLow; high = alphaHigh; }
    if (high - low < max(0.0312, high * EdgeThreshold)) return center;
    float2 direction = float2(-((lNW + lNE) - (lSW + lSE)), (lNW + lSW) - (lNE + lSE));
    float reduction = max((lNW + lNE + lSW + lSE) * (0.25 * 0.125), 1.0 / 128.0);
    direction = clamp(direction / (min(abs(direction.x), abs(direction.y)) + reduction), -8, 8) * TexelSize;
    float4 a = 0.5 * (Source.Sample(ImageSampler, uv + direction * (-1.0 / 6)) + Source.Sample(ImageSampler, uv + direction * (1.0 / 6)));
    float4 b = a * 0.5 + 0.25 * (Source.Sample(ImageSampler, uv - direction * 0.5) + Source.Sample(ImageSampler, uv + direction * 0.5));
    float lb = coverageEdge ? b.a : Luma(b.rgb);
    return lb < low || lb > high ? a : b;
}
technique Raster { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 RasterPS(); } }
technique FXAA { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 FXAAPS(); } }

float2 ResolveScale;
float4 ResolvePS(PSIn input) : SV_Target
{
    if (all(ResolveScale <= 1)) return Source.SampleLevel(ImageSampler, input.UV, 0);
    float2 scale = max(ResolveScale, 1);
    float2 low = input.UV / TexelSize - scale * 0.5;
    float2 high = low + scale;
    float2 first = floor(low);
    float4 result = 0; float totalWeight = 0;
    [loop] for (int y = 0; y <= (int)ceil(scale.y); y++) [loop] for (int x = 0; x <= (int)ceil(scale.x); x++)
    {
        float2 cell = first + float2(x, y);
        float2 coverage = max(0, min(cell + 1, high) - max(cell, low));
        float weight = coverage.x * coverage.y;
        result += Source.SampleLevel(ImageSampler, (cell + 0.5) * TexelSize, 0) * weight;
        totalWeight += weight;
    }
    return result / max(totalWeight, 1e-6);
}
technique Resolve { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 ResolvePS(); } }

