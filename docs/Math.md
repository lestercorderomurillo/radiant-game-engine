# Backend-independent math

Radiant's public vector and matrix signatures now use `System.Numerics.Vector2`,
`Vector3`, `Vector4` and `Matrix4x4`. This includes ECS positions/spatial queries,
movement components, transforms, cameras, trace geometry, renderer parameters and
compute push-constant values. These are actual .NET types in the compiled API, not
renamed XNA types or wrappers with a MonoGame dependency.

```csharp
using System.Numerics;
using com.radiant.engine.bundle;
using com.radiant.engine.core;

int entity = scene.ECS.CreateEntity(new Vector3(2, 0, -5));
ref var transform = ref scene.ECS.GetComponent<Transform3D>(entity);
transform.Scale = new Vector3(2);
transform.Rotation = new Vector3(0, MathF.PI / 2, 0);
Matrix4x4 world = transform.Matrix;
```

Spatial positions changed directly through component references must still be synced
through `ECS.SetPosition`, or by the movement systems, before spatial queries.

## Conventions

- World coordinates are right-handed, Y up, forward along negative Z.
- Angles are radians. Euler camera rotation uses yaw/pitch/roll.
- Transforms use row vectors: `scale * rotation * translation`.
- View/projection is `view * projection`; near/far depth maps to 0/1.
- `Vector2`, `Vector3`, `Vector4` occupy 8, 12 and 16 bytes respectively; `Matrix4x4`
  occupies 64. This does not imply arbitrary structures are automatically std430-safe.

Use `com.radiant.engine.math.MatrixMath.Invert` for inverse matrices supplied to the
renderer. It computes cofactors with double intermediates and explicit float rounding.
A direct substitution of the SIMD `Matrix4x4.Invert` produced small rounding changes
that moved primary rays across 2D triangle boundaries. The shared helper preserves
that behavior without calling MonoGame. It rejects singular/nonfinite matrices and
inverses that cannot be represented as finite floats. Other math uses System.Numerics.

## Mesh data

`Mesh` now accepts Radiant `MeshVertex[]` instead of MonoGame vertices:

```csharp
var mesh = new Mesh(
[
    new MeshVertex(new(-1, 0, 0), Vector3.UnitZ, new(0, 1)),
    new MeshVertex(new(1, 0, 0), Vector3.UnitZ, new(1, 1)),
    new MeshVertex(new(0, 1, 0), Vector3.UnitZ, new(0.5f, 0))
], [0, 1, 2]);
```

`MeshVertex` contains position, normal and UV, with a 32-byte layout. Mesh copies the
arrays; backend GPU buffers remain owned by the Renderer. MonoGame vertex declarations
are used only when uploading/drawing. The same CPU mesh can feed another backend.

## Colors and integer geometry

`com.radiant.engine.math.Color`, `Point` and `Rectangle` are Radiant structs. Materials,
mesh colors, UI bounds, resolutions, renderer tints and texture uploads use these types.

```csharp
using com.radiant.engine.math;

var tint = new Color(255, 180, 100, 128);
var bounds = new Rectangle(10, 20, 320, 180);
bool hovered = bounds.Contains(scene.Controls.Mouse.Position);
```

- `Color` is four consecutive bytes: R, G, B, A. `PackedValue` stores R in the least
  significant byte. Integer channels clamp to 0–255; normalized float constructors
  clamp to 0–1 and truncate after multiplying by 255. Constructors perform no gamma
  conversion or premultiplication. `default(Color)` is transparent black.
- `Point` contains two integers (8 bytes); `ToVector2()` returns System.Numerics.
- `Rectangle` contains X, Y, Width, Height (16 bytes). Point containment includes
  left/top and excludes right/bottom. Touching rectangles do not intersect.
- `ColorVertex` contains a Numerics position and Radiant color (16 bytes). The
  MonoGame backend retains its matching native vertex declaration for drawing.

Color arrays upload/read back directly through the existing texture API with no
conversion buffer. Scalar native conversions live in the internal backend adapter.

## Existing project migration

This is a source/API change. Rebuild consumers and replace XNA vector/matrix imports
with System.Numerics, and import `com.radiant.engine.math` for Radiant colors and
integer geometry. These value types contain no MonoGame conversions or dependencies.

- XNA `Matrix` → `System.Numerics.Matrix4x4`.
- `Matrix.Invert(m)` → `MatrixMath.Invert(m)` for rendering inverses.
- `matrix.Determinant()` → `matrix.GetDeterminant()`.
- `Vector3.Up` / `Right` / `Forward` → `UnitY` / `UnitX` / `-UnitZ`.
- `vector.Normalize()` → `vector = Vector3.Normalize(vector)` (or Vector2).
- `MathHelper` constants/clamping → `MathF.PI`, `Math.Clamp`, etc.
- `VertexPositionNormalTexture[]` → `MeshVertex[]`.
- `VertexPositionColor[]` → `ColorVertex[]`.
- XNA `Color`, `Point`, `Rectangle` → the same names in `com.radiant.engine.math`.
- Color/point vector conversion returns System.Numerics directly; remove `.ToNumerics()` afterwards.

The repository uses internal compilation aliases in `eng/Radiant.Numerics.cs` to keep
existing implementation spelling concise. Consumer projects do not need this file;
use the standard public .NET type names directly. The aliases refer to the actual public types.

The MonoGame renderer converts scalars at its native API calls. Vector/matrix array
parameters use cached conversion buffers associated with EffectParameter objects,
avoiding a new conversion array every frame. The array overloads preserve MonoGame's
matrix upload/transposition handling.

## Verification and remaining boundaries

The CPU regression scans exported signatures to reject XNA vector/matrix/color/point/rectangle types,
including arrays and generic arguments. It checks mesh layout, matrix memory layout,
transform order, view/projection conventions, precision-sensitive inversion, RGBA byte
order/quantization, vertex packing and rectangle hit testing.
GPU checks cover compute, raster geometry, 2D and 3D pipelines and ray tracing.

Render states now use [Radiant descriptions](RenderStates.md). Texture/effect types
and the concrete Renderer remain coupled to MonoGame. This math migration does not claim that an
independent graphics backend or a dependency-free core package already exists.
