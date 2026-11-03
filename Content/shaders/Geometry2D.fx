float4x4 ViewProjection;
float4x4 View;
struct VSIn { float3 Position : POSITION0; float4 Color : COLOR0; };
struct PSIn { float4 Position : SV_POSITION; float4 Color : COLOR0; float Depth : TEXCOORD0; };
PSIn VS(VSIn input) { PSIn o; o.Position = mul(float4(input.Position, 1), ViewProjection); o.Color = input.Color; o.Depth = -mul(float4(input.Position, 1), View).z / 200; return o; }
float4 PS(PSIn input) : SV_Target { return input.Color; }
technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }

float4 DepthPS(PSIn input) : SV_Target { return float4(input.Depth.xxx, 1); }
technique DepthOnly { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 DepthPS(); } }
