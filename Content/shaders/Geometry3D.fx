float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 NormalMatrix;
float4 BaseColor;
float FarPlane;

struct VertexInput
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 UV : TEXCOORD0;
};
struct PixelInput
{
    precise float4 Position : SV_POSITION;
    float3 Normal : TEXCOORD0;
    float Depth : TEXCOORD1;
};
PixelInput MainVS(VertexInput input)
{
    PixelInput output;
    precise float4 worldPosition = mul(float4(input.Position, 1), World);
    precise float4 viewPosition = mul(worldPosition, View);
    output.Position = mul(viewPosition, Projection);
    output.Normal = mul(float4(input.Normal, 0), NormalMatrix).xyz;
    output.Depth = -viewPosition.z / FarPlane;
    return output;
}
struct GeometryOutput
{
    float4 Color : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Depth : SV_Target2;
};
GeometryOutput MainPS(PixelInput input)
{
    GeometryOutput output;
    output.Color = float4(BaseColor.rgb, 1);
    output.Normal = float4(normalize(input.Normal) * 0.5 + 0.5, 1);
    output.Depth = float4(input.Depth.xxx, 1);
    return output;
}
technique Default
{
    pass P0
    {
        VertexShader = compile vs_6_0 MainVS();
        PixelShader = compile ps_6_0 MainPS();
    }
}

float4 ColorPS(PixelInput input) : SV_Target { return float4(BaseColor.rgb, 1); }
struct TwoOutputs { float4 Color : SV_Target0; float4 Data : SV_Target1; };
TwoOutputs NormalPS(PixelInput input) { TwoOutputs o; o.Color = float4(BaseColor.rgb, 1); o.Data = float4(normalize(input.Normal) * 0.5 + 0.5, 1); return o; }
TwoOutputs DepthPS(PixelInput input) { TwoOutputs o; o.Color = float4(BaseColor.rgb, 1); o.Data = float4(input.Depth.xxx, 1); return o; }
technique ColorOnly { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 ColorPS(); } }
technique ColorNormals { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 NormalPS(); } }
technique ColorDepth { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 DepthPS(); } }

float4 NormalOnlyPS(PixelInput input) : SV_Target { return float4(normalize(input.Normal) * 0.5 + 0.5, 1); }
float4 DepthOnlyPS(PixelInput input) : SV_Target { return float4(input.Depth.xxx, 1); }
technique NormalsOnly { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 NormalOnlyPS(); } }
technique DepthOnly { pass P0 { VertexShader = compile vs_6_0 MainVS(); PixelShader = compile ps_6_0 DepthOnlyPS(); } }
