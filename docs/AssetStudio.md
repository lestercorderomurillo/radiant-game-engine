# Asset Studio

Create shaders, sprites and 3D models directly in the engine. Asset Studio is the in-engine content workspace:
what you make there is saved into your project and shows up in the running scene without an export, import or
restart in between.

## Shaders

Write and edit shader passes next to the scene that uses them. Changes recompile on save and the renderer
picks up the new effect on the next frame, so you can tune a look while watching it.

## Sprites

Paint and edit 2D sprites and textures in place, then assign them to a `Material` on any entity. Sprite edits
update every entity that shares the texture.

## 3D models

Build and adjust meshes without leaving Radiant, from simple blockouts to detailed models, and drop them into the
scene as `Mesh3D` entities. Models round-trip with Blender when you need a full modeling suite.

## Continue

- [Resources and textures](GraphicsResources.md): how textures and targets are created and owned.
- [Inspector](Inspector.md): live editing of the entities that use your assets.
