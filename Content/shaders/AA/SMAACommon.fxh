float4 Metrics;
#define SMAA_RT_METRICS Metrics
#define SMAA_HLSL_4
#define SMAA_PRESET_HIGH
#include "SMAA/SMAA.hlsl"
struct VSIn { float3 Position : POSITION0; float2 UV : TEXCOORD0; };
struct PSIn { float4 Position : SV_POSITION; float2 UV : TEXCOORD0; float2 Pixel : TEXCOORD1; float4 Offset0 : TEXCOORD2; float4 Offset1 : TEXCOORD3; float4 Offset2 : TEXCOORD4; };

PSIn EdgeVS(VSIn input) { PSIn o = (PSIn)0; o.Position = float4(input.Position, 1); o.UV = input.UV; float4 offsets[3]; SMAAEdgeDetectionVS(input.UV, offsets); o.Offset0 = offsets[0]; o.Offset1 = offsets[1]; o.Offset2 = offsets[2]; return o; }

PSIn WeightVS(VSIn input) { PSIn o = (PSIn)0; o.Position = float4(input.Position, 1); o.UV = input.UV; float4 offsets[3]; SMAABlendingWeightCalculationVS(input.UV, o.Pixel, offsets); o.Offset0 = offsets[0]; o.Offset1 = offsets[1]; o.Offset2 = offsets[2]; return o; }

PSIn BlendVS(VSIn input) { PSIn o = (PSIn)0; o.Position = float4(input.Position, 1); o.UV = input.UV; SMAANeighborhoodBlendingVS(input.UV, o.Offset0); return o; }
