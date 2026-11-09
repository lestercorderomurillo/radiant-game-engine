float4x4 World;
float4x4 View;
float4x4 Projection;
float4x4 NormalMatrix;
float4x4 ViewProjection;
float4 BaseColor;
float3 LightDirection;
float3 LightColor;
float Ambient;
struct MeshInput { float3 Position : POSITION0; float3 Normal : NORMAL0; float2 UV : TEXCOORD0; };
struct ShapeInput { float3 Position : POSITION0; float4 Color : COLOR0; };
struct PixelInput { precise float4 Position : SV_POSITION; float3 Normal : TEXCOORD0; float4 Color : COLOR0; };
PixelInput MeshVS(MeshInput input)
{
    PixelInput output;
    precise float4 position = mul(float4(input.Position, 1), World);
    position = mul(position, View);
    output.Position = mul(position, Projection);
    output.Normal = mul(float4(input.Normal, 0), NormalMatrix).xyz;
    output.Color = float4(BaseColor.rgb, 1);
    return output;
}

PixelInput ShapeVS(ShapeInput input)
{
    PixelInput output;
    output.Position = mul(float4(input.Position, 1), ViewProjection);
    output.Normal = float3(0, 0, 1);
    output.Color = input.Color;
    return output;
}

float4 PS(PixelInput input) : SV_Target
{
    float diffuse = max(dot(normalize(input.Normal), LightDirection), 0);
    return float4(input.Color.rgb * (Ambient + diffuse * LightColor), input.Color.a);
}

technique Meshes { pass P0 { VertexShader = compile vs_6_0 MeshVS(); PixelShader = compile ps_6_0 PS(); } }
technique Shapes { pass P0 { VertexShader = compile vs_6_0 ShapeVS(); PixelShader = compile ps_6_0 PS(); } }
