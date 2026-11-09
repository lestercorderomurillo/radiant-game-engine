# Math

Radiant uses the standard .NET math types for vectors and matrices, plus a few small types of its own for
colors and pixel rectangles.

## Vectors and matrices

Positions, sizes, velocities and shader parameters are `System.Numerics` types: `Vector2`, `Vector3`,
`Vector4` and `Matrix4x4`.

The following example shows how to place, scale and rotate a 3D entity:

```csharp
using System.Numerics;

int statue = ECS.CreateEntity(new Vector3(2, 0, -5));
ref var transform = ref ECS.GetComponent<Transform3D>(statue);
transform.Scale = new Vector3(2);
transform.Rotation = new Vector3(0, MathF.PI / 2, 0); // Quarter turn around Y
Matrix4x4 world = transform.Matrix;
```

Keep these conventions in mind:

- The world is right-handed with Y up; forward is negative Z.
- Angles are radians.
- Matrices compose left to right: `scale * rotation * translation`, then `view * projection`.

For inverse matrices you pass to the renderer, such as an inverse view-projection, use
`MatrixMath.Invert(matrix)`. It gives the same result on every machine, which keeps ray-traced edges stable.

## Colors

`Color` stores red, green, blue and alpha as bytes.

The following example shows the ways to build and adjust colors:

```csharp
using com.radiant.engine.math;

var warm = new Color(255, 180, 100);            // Bytes, opaque
var glass = new Color(255, 60, 60, 160);       // Bytes with alpha
var sky = new Color(0.4f, 0.6f, 1f);           // 0–1 floats
var dim = warm * 0.5f;                         // Scales every channel, alpha included
Vector4 forShader = warm.ToVector4();          // 0–1 values for SetParameter
```

Named colors include `White`, `Black`, `Transparent`, `Red`, `Orange`, `OrangeRed`, `Gold`, `Lime`, `Coral`,
`CornflowerBlue` and `Gray`. `default(Color)` is transparent black.

## Rectangles and points

`Rectangle` and `Point` hold integer pixel geometry for UI, sprites and texture regions.

The following example shows how to detect a hover over a button:

```csharp
var button = new Rectangle(10, 20, 320, 48);
bool hovered = button.Contains(Controls.Mouse.Position);
```

A rectangle contains its left and top edges but not its right and bottom edges, so neighboring rectangles never
overlap.

## Meshes

A `Mesh` is built from `MeshVertex` values (position, normal, texture coordinate) and triangle indices. Many
entities can share one mesh through `Mesh3D`.

The following example shows how to build a single triangle facing the camera:

```csharp
var triangle = new Mesh(
[
    new MeshVertex(new(-1, 0, 0), Vector3.UnitZ, new(0, 1)),
    new MeshVertex(new(1, 0, 0), Vector3.UnitZ, new(1, 1)),
    new MeshVertex(new(0, 1, 0), Vector3.UnitZ, new(0.5f, 0))
], [0, 1, 2]);
```

For colored triangles drawn directly, use `ColorVertex` with `Renderer.DrawTriangles`.
