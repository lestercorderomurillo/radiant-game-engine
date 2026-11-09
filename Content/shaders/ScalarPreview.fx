Texture2D Source : register(t0);
SamplerState ImageSampler : register(s0);
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; };

PSIn VS(VSIn input) { PSIn o; o.Position = float4(input.Position, 1); o.UV = input.UV; return o; }

float4 PS(PSIn input) : SV_Target { float value = Source.SampleLevel(ImageSampler, input.UV, 0).r; return float4(value.xxx, 1); }
technique Default { pass P0 { VertexShader = compile vs_6_0 VS(); PixelShader = compile ps_6_0 PS(); } }
