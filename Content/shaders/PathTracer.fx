Texture2D SceneData : register(t0);
Texture2D History : register(t1);
SamplerState DataSampler : register(s0);
SamplerState HistorySampler : register(s1);
float2 DataSize;
int DataWidth;
int TriangleOffset;
int NodeCount;
int SampleIndex;
int SequenceIndex;
int MaxBounces;
float4x4 InverseViewProjection;
float3 CameraPosition;
bool Orthographic;
float3 Environment;
float2 OutputSize;
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };
PSIn VS(VSIn input) { PSIn o; o.Position = float4(input.Position, 1); o.UV = input.UV; return o; }
float4 Data(int index) { return SceneData.SampleLevel(DataSampler, (float2(index % DataWidth, index / DataWidth) + 0.5) / DataSize, 0); }
uint Hash(uint v) { v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; return v ^ (v >> 16); }
float Random(inout uint state) { state = Hash(state + 0x9e3779b9u); return (state >> 8) * (1.0 / 16777216.0); }
bool Bounds(float3 origin, float3 direction, float3 low, float3 high, float closest)
{
    float tMin = 0.0001, tMax = closest;
    [unroll] for (int axis = 0; axis < 3; axis++)
    {
        if (abs(direction[axis]) < 1e-8) { if (origin[axis] < low[axis] || origin[axis] > high[axis]) return false; }
        else
        {
            float a = (low[axis] - origin[axis]) / direction[axis], b = (high[axis] - origin[axis]) / direction[axis];
            tMin = max(tMin, min(a, b)); tMax = min(tMax, max(a, b));
            if (tMax < tMin) return false;
        }
    }
    return true;
}
bool Intersect(float3 origin, float3 direction, float maxDistance, out float distance, out float3 normal, out float3 albedo, out float3 emission)
{
    distance = maxDistance; normal = albedo = emission = 0;
    bool hit = false;
    int node = 0;
    [loop] for (int traversal = 0; traversal < NodeCount && node < NodeCount; traversal++)
    {
        float4 low = Data(node * 3), high = Data(node * 3 + 1);
        int count = (int)Data(node * 3 + 2).x;
        if (!Bounds(origin, direction, low.xyz, high.xyz, distance)) { node = max(node + 1, (int)high.w); continue; }
        [loop] for (int i = 0; i < count; i++)
        {
            int k = TriangleOffset + ((int)low.w + i) * 5;
            float3 a = Data(k).xyz, e1 = Data(k + 1).xyz, e2 = Data(k + 2).xyz;
            float3 p = cross(direction, e2);
            float determinant = dot(e1, p);
            if (abs(determinant) < 1e-8) continue;
            float3 t = origin - a;
            float u = dot(t, p) / determinant;
            if (u < 0 || u > 1) continue;
            float3 q = cross(t, e1);
            float v = dot(direction, q) / determinant;
            float d = dot(e2, q) / determinant;
            if (v < 0 || u + v > 1 || d <= 0.0001 || d >= distance) continue;
            hit = true; distance = d;
            normal = normalize(cross(e1, e2));
            if (dot(normal, direction) > 0) normal = -normal;
            albedo = Data(k + 3).xyz; emission = Data(k + 4).xyz;
        }
        node++;
    }
    return hit;
}
float3 Hemisphere(float3 n, inout uint state)
{
    float u = Random(state), angle = 6.28318530718 * Random(state);
    float3 tangent = normalize(cross(abs(n.z) < 0.999 ? float3(0, 0, 1) : float3(0, 1, 0), n));
    return normalize(tangent * (sqrt(u) * cos(angle)) + cross(n, tangent) * (sqrt(u) * sin(angle)) + n * sqrt(1 - u));
}
float3 TraceRadiance(PSIn input)
{
    uint state = Hash((uint)input.Position.x + (uint)input.Position.y * (uint)OutputSize.x + (uint)SequenceIndex * 747796405u);
    float2 uv = input.UV + (float2(Random(state), Random(state)) - 0.5) / OutputSize;
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
