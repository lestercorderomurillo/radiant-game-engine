// UDR4: jitter-aware radiance reconstruction and conservative temporal reuse.
Texture2D InputTexture : register(t0);
Texture2D HistoryTexture : register(t1);
Texture2D MotionTexture : register(t2);
Texture2D EmissiveTexture : register(t3);
Texture2D AbsorptionTexture : register(t4);
Texture2D GeometryHistory : register(t5);
SamplerState LinearSampler : register(s0);
float2 InputSize;
float2 OutputSize;
float2 Jitter;
float HasHistory;
float HasGeometry;
float HasMotion;
float MaxHistory;
float Sharpness;
float PreserveAlpha;
float4x4 CurrentScreenToWorld;
float4x4 PreviousWorldToScreen;

struct VertexInput { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PixelInput { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };
PixelInput MainVS(VertexInput input)
{
    PixelInput output;
    output.Position = float4(input.Position, 1);
    output.UV = input.UV;
    return output;
}

float Luma(float3 color) { return dot(color, float3(0.2126, 0.7152, 0.0722)); }

float4 Guide(float2 uv)
{
    if (HasGeometry < 0.5) return 0;

    float4 emission = EmissiveTexture.SampleLevel(LinearSampler, uv, 0);
    float4 absorption = AbsorptionTexture.SampleLevel(LinearSampler, uv, 0);
    return float4(Luma(emission.rgb), emission.a, absorption.a, Luma(absorption.rgb));
}

float Maximum(float4 value) { return max(max(value.x, value.y), max(value.z, value.w)); }

float Kernel(float x)
{
    x = abs(x);

    if (x < 1) return 1.5 * x*x*x - 2.5 * x*x + 1;
    if (x < 2) return -0.5 * x*x*x + 2.5 * x*x - 4*x + 2;

    return 0;
}

float4 SpatialPS(PixelInput input) : SV_Target0
{
    // HRC evaluated texel i at i + 0.5 + Jitter; undo that offset here.
    float2 position = input.UV * InputSize - Jitter - 0.5;
    float2 basePixel = floor(position);
    float2 fraction = position - basePixel;
    float4 centerGuide = Guide(input.UV);
    float4 result = 0;
    float weightSum = 0;
    float4 minimum = 1e20;
    float4 maximum = -1e20;
    [unroll] for (int y = -1; y <= 2; y++)
    {
        [unroll] for (int x = -1; x <= 2; x++)
        {
            int2 pixel = clamp(int2(basePixel) + int2(x,y), int2(0,0), int2(InputSize)-1);
            float4 color = max(InputTexture.Load(int3(pixel,0)), 0);
            float2 sampleUV = (float2(pixel) + 0.5 + Jitter) / InputSize;
            float difference = Maximum(abs(Guide(sampleUV) - centerGuide));
            float weight = Kernel(x - fraction.x) * Kernel(y - fraction.y) / (1 + 8*difference);
            result += color * weight;
            weightSum += weight;
            if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
            {
                minimum = min(minimum, color);
                maximum = max(maximum, color);
            }
        }
    }

    float4 fallback = InputTexture.SampleLevel(LinearSampler, input.UV - Jitter / InputSize, 0);
    return clamp(abs(weightSum) > 0.0001 ? result / weightSum : fallback, minimum, maximum);
}

float4 TemporalPS(PixelInput input) : SV_Target0
{
    float2 uv = input.UV;
    float3 current = InputTexture.SampleLevel(LinearSampler, uv, 0).rgb;

    if (HasHistory < 0.5) return float4(current, 1);

    float2 motion = HasMotion > 0.5 ? MotionTexture.SampleLevel(LinearSampler, uv, 0).rg : float2(0,0);
    float4 world = mul(float4(uv * OutputSize, 0, 1), CurrentScreenToWorld);
    world.xy -= motion;
    float2 previousUV = mul(world, PreviousWorldToScreen).xy / OutputSize;
    float2 margin = 0.5 / OutputSize;

    if (any(previousUV < margin) || any(previousUV > 1-margin)) return float4(current, 1);

    float4 old = HistoryTexture.SampleLevel(LinearSampler, previousUV, 0);
    float3 minimum = current;
    float3 maximum = current;
    float3 sum = 0;
    float3 squareSum = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float3 neighbor = InputTexture.SampleLevel(LinearSampler, uv + float2(x,y)/OutputSize, 0).rgb;
            minimum = min(minimum, neighbor);
            maximum = max(maximum, neighbor);
            sum += neighbor;
            squareSum += neighbor*neighbor;
        }
    }

    float3 mean = sum / 9;
    float3 deviation = sqrt(max(squareSum/9 - mean*mean, 0));
    float3 history = clamp(old.rgb, max(minimum, mean - 1.5*deviation), min(maximum, mean + 1.5*deviation));

    float guideDifference = Maximum(abs(Guide(uv) - GeometryHistory.SampleLevel(LinearSampler, previousUV, 0)));
    float validity = HasGeometry > 0.5 ? 1 - smoothstep(0.03, 0.2, guideDifference) : 1;

    // Light and shadow motion need not match object motion. Respond to radiance changes too.
    float difference = length(current-old.rgb) / (0.05 + length(current) + length(old.rgb));
    float reactive = smoothstep(0.04, 0.3, difference);
    float age = min(old.a + 1, MaxHistory);
    float weight = (1 - 1/max(age,1)) * validity * (1-reactive);
    return float4(lerp(current, history, weight), lerp(1, age, validity*(1-reactive)));
}

float4 GuidePS(PixelInput input) : SV_Target0 { return Guide(input.UV); }

float4 SharpenPS(PixelInput input) : SV_Target0
{
    float2 uv = input.UV;
    float2 texel = 1/OutputSize;
    float3 center = InputTexture.SampleLevel(LinearSampler, uv, 0).rgb;
    float3 north = InputTexture.SampleLevel(LinearSampler, uv + float2(0,-texel.y), 0).rgb;
    float3 south = InputTexture.SampleLevel(LinearSampler, uv + float2(0,texel.y), 0).rgb;
    float3 east = InputTexture.SampleLevel(LinearSampler, uv + float2(texel.x,0), 0).rgb;
    float3 west = InputTexture.SampleLevel(LinearSampler, uv + float2(-texel.x,0), 0).rgb;
    float3 minimum = min(center,min(min(north,south),min(east,west)));
    float3 maximum = max(center,max(max(north,south),max(east,west)));
    float3 detail = center - (north+south+east+west)*0.25;
    return float4(clamp(center + detail*Sharpness, minimum, maximum), PreserveAlpha > 0.5 ? saturate(InputTexture.SampleLevel(LinearSampler, uv, 0).a) : 1);
}

technique Spatial { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 SpatialPS(); } }
technique Temporal { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 TemporalPS(); } }
technique StoreGeometry { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 GuidePS(); } }
technique Sharpen { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 SharpenPS(); } }
