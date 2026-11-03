# SMAA reference implementation

Upstream: https://github.com/iryoku/smaa (SMAA 2.8). Copyright and permission are preserved in LICENSE.txt and SMAA.hlsl.

Radiant adaptations:

- Converted the original Latin-1 source text to UTF-8.
- Replaced effect-language sampler state blocks with an explicit slot-zero sampler. The edge pass supplies point filtering; weights and blending supply linear filtering. SMAA 1x uses those states in separate passes.
- Added alpha-coverage edge detection in the engine edge wrapper so black geometry on transparent targets can be filtered before compositing. The upstream color detector is retained for fully covered regions.
- Split the engine wrappers into separate effects with contiguous texture slots for MonoGame's Vulkan resource binding.
- Converted the upstream AreaTexDX10 and SearchTex DDS lookup data losslessly to RGBA PNG because MonoGame's importer does not accept the search texture's DDS pixel format. No mipmaps, alpha premultiplication or compression are applied by the content pipeline.

Only SMAA 1x is exposed here. TAA, geometry multisampling and supersampling are separate engine systems rather than renamed SMAA variants.
