#include "SMAACommon.fxh"
Texture2D Edges : register(t0);
Texture2D Area : register(t1);
Texture2D Search : register(t2);

float4 PS(PSIn input) : SV_Target { float4 offsets[3] = { input.Offset0, input.Offset1, input.Offset2 }; return SMAABlendingWeightCalculationPS(input.UV, input.Pixel, offsets, Edges, Area, Search, 0); }
technique Default { pass P0 { VertexShader = compile vs_6_0 WeightVS(); PixelShader = compile ps_6_0 PS(); } }
