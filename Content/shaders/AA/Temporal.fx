Texture2D Source : register(t0);
Texture2D History : register(t1);
Texture2D Depth : register(t2);
Texture2D HistoryDepth : register(t3);
Texture2D Motion : register(t4);
SamplerState LinearSampler : register(s0);
float4 PointFetch(Texture2D texture, float2 uv)
{
    uint width, height; texture.GetDimensions(width, height);
    float2 size = float2(width, height);
    return texture.SampleLevel(LinearSampler, (floor(uv * size) + 0.5) / size, 0);
}
float2 TexelSize;
float4x4 InverseViewProjection;
float4x4 CurrentProjection;
float4x4 PreviousViewProjection;
float4x4 PreviousView;
float FarPlane;
float PreviousFar;
bool HasDepth;
bool HasMotion;
bool HasHistory;
float HistoryWeight;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };
PSIn VS(VSIn input) { PSIn o; o.Position = float4(input.Position, 1); o.UV = input.UV; return o; }
struct Outputs { float4 Color : SV_Target0; float4 Depth : SV_Target1; };
Outputs PS(PSIn input)
{
    float2 uv = input.UV;
    float4 current = Source.SampleLevel(LinearSampler, uv, 0);
    float depth = HasDepth ? PointFetch(Depth, uv).r : 0;
    float2 oldUV = uv;
    bool valid = HasHistory;
    float expectedDepth = depth;
    if (HasDepth && depth > 0)
    {
        float4 projected = mul(float4(0, 0, -depth * FarPlane, 1), CurrentProjection);
        float4 world = mul(float4(uv.x * 2 - 1, 1 - uv.y * 2, projected.z / projected.w, 1), InverseViewProjection);
        world /= world.w;
        float4 oldClip = mul(world, PreviousViewProjection);
        valid = valid && oldClip.w > 0;
        oldUV = oldClip.xy / max(oldClip.w, 1e-6) * float2(0.5, -0.5) + 0.5;
        expectedDepth = -mul(world, PreviousView).z / max(PreviousFar, 1e-6);
    }
    if (HasMotion) oldUV = uv + PointFetch(Motion, uv).rg;
    valid = valid && all(oldUV >= 0) && all(oldUV <= 1);
    if (valid && HasDepth)
    {
        float previous = PointFetch(HistoryDepth, oldUV).r;
        valid = (depth == 0 && previous == 0) || (depth > 0 && previous > 0 && abs(previous - expectedDepth) < max(0.0005, abs(expectedDepth) * 0.01));
    }
    float4 low = current, high = current;
    [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
    {
        float4 sampleColor = Source.SampleLevel(LinearSampler, uv + float2(x, y) * TexelSize, 0);
        low = min(low, sampleColor); high = max(high, sampleColor);
    }
    float4 old = valid ? History.SampleLevel(LinearSampler, oldUV, 0) : current;
    old = clamp(old, low, high);
    Outputs result;
    result.Color = lerp(current, old, valid ? HistoryWeight : 0);
    result.Depth = float4(depth.xxx, 1);
    return result;
}
technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }
