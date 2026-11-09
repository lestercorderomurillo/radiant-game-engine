#include "SMAACommon.fxh"
Texture2D Source : register(t0);
Texture2D Weights : register(t1);

float4 PS(PSIn input) : SV_Target { return SMAANeighborhoodBlendingPS(input.UV, input.Offset0, Source, Weights); }
technique Default { pass P0 { VertexShader = compile vs_6_0 BlendVS(); PixelShader = compile ps_6_0 PS(); } }
