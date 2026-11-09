using System.Linq;
using System;
using System.Collections.Generic;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

RuntimeBackendRegression.Run();
InputRegression.Run();
MathBackendRegression.Run();
RenderStateRegression.Run();
GraphicsResourceRegression.Run();
RendererContractRegression.Run();
RenderViewRegression.Run();
ResourceCacheRegression.Run();
ShapeCollectionRegression.Run();
FontServiceRegression.Run();
PortableRendererRegression.Run();

var Scene = new Scene();
Scene.Initialize();
Scene.ECS.AddSystem<PerlinNoise2D>();

var World = Scene.ECS.AddSystem<WorldGen>();
Scene.SetupECS();
World.GenerateWorld(8, 128);

var Entities = new List<int>();
Scene.ECS.GetAllEntityIds(Entities);

if (Entities.Count != 8 * 128 * 2) throw new Exception("World entity count is incorrect.");

foreach (int Entity in Entities)
{
    ref var Tile = ref Scene.ECS.GetComponent<TileData>(Entity);
    ref var Rectangle = ref Scene.ECS.GetComponent<Rectangle2D>(Entity);

    if (string.IsNullOrEmpty(Tile.TileTypeId) || Rectangle.Size != new Vector2(World.TileSize))
        throw new Exception("Tile components were lost during archetype migration.");
    if (Scene.ECS.AtExact(Tile.X * World.TileSize, Tile.Y * World.TileSize, Tile.Layer) != Entity)
        throw new Exception("Tile spatial lookup is incorrect.");
}

int Light = LightFactory.CreateLight(Scene.ECS, new Vector2(1920, 1080), 100, Color.Orange, Color.Orange);

if (Scene.ECS.GetComponent<Circle2D>(Light).Radius != 100 || Scene.ECS.GetComponent<Transform2D>(Light).Position.X != 1920)
    throw new Exception("Light components were lost during archetype migration.");

Scene.Dispose();

var Loop = new GameLoop();
Loop.Initialize();
Loop.Dispose();
Console.WriteLine("PASS: 2048 populated tiles, spatial lookups, light creation, platform-safe timer lifecycle.");

// Temporal motion must use the immediately preceding rendered position, including ring wrap.
var motion = new MotionTrackable();

if (motion.RenderedDisplacement(new Vector2(900, 10)) != Vector2.Zero)
    throw new Exception("New motion trackers must start without a fabricated velocity.");

for (int frame = 0; frame < 12; frame++)
    motion.Push(new Vector2(frame * 37, -frame * 19));

if (motion.RenderedDisplacement(new Vector2(12 * 37, -12 * 19)) != new Vector2(37, -19))
    throw new Exception("Temporal motion uses stale history or clips fast movement.");

var jitterSamples = new HashSet<Vector2>();

for (int frame = 0; frame < 8; frame++)
{
    var jitter = UDR4.JitterSample(frame);

    if (MathF.Abs(jitter.X) > 0.5f || MathF.Abs(jitter.Y) > 0.5f || jitter != UDR4.JitterSample(frame + 8))
        throw new Exception("Reconstruction jitter is outside its texel or is not periodic.");

    jitterSamples.Add(jitter);
}

if (jitterSamples.Count != 8) throw new Exception("Reconstruction jitter repeats samples within its cycle.");

Console.WriteLine("PASS: temporal displacement after history wrap, fast signed motion, and subpixel jitter sequence.");

// 3D transforms must apply scale, rotation (radians), then translation.
var transform3D = new Transform3D
{
    Scale = new Vector3(2, 3, 4), Rotation = new Vector3(0, (global::System.MathF.PI / 2), 0),
    Position = new Vector3(10, 20, 30)
};

if (Vector3.Distance(Vector3.Transform(Vector3.UnitX, Geometry3D.WorldMatrix(transform3D)),
    new Vector3(10, 20, 28)) > 0.0001f)
    throw new Exception("3D world transform order is incorrect.");

var camera3D = new Camera3D();
var cameraOrigin = Vector3.Transform(camera3D.Position, camera3D.View);

if (cameraOrigin.Length() > 0.0001f || camera3D.Projection(16f / 9f).M11 <= 0)
    throw new Exception("3D camera view/projection is incorrect.");
if (Mesh.Cube.VertexCount != 24 || Mesh.Cube.TriangleCount != 12 || Mesh.Plane.TriangleCount != 2)
    throw new Exception("3D primitives have incomplete geometry.");

bool invalidMeshRejected = false;

try
{
    _ = new Mesh(new MeshVertex[3], new[] { 0, 1, 3 });
}
catch (ArgumentOutOfRangeException) { invalidMeshRejected = true; }

if (!invalidMeshRejected) throw new Exception("Mesh accepted an out-of-range index.");

Console.WriteLine("PASS: 3D transforms, camera matrices, primitive topology, and invalid mesh rejection.");

if (System.Runtime.InteropServices.Marshal.SizeOf<Transform2D>() != 24 || System.Runtime.InteropServices.Marshal.SizeOf<Transform3D>() != 36)
    throw new Exception("Dimension-specific transform storage regressed.");

using (var dimensions = new DisposableScene())
{
    var ecs = dimensions.Value.ECS;
    int entity2D = ecs.CreateEntity(new Vector2(1, 2), 7);
    int entity3D = ecs.CreateEntity(new Vector3(1, 2, 3));

    if (ecs.HasComponent<Transform3D>(entity2D) || ecs.HasComponent<Transform2D>(entity3D)) throw new Exception("Creating an entity adds data from another dimension.");
    if (ecs.GetComponent<Transform2D>(entity2D).Scale != Vector2.One || ecs.GetComponent<Transform3D>(entity3D).Scale != Vector3.One) throw new Exception("Transform constructors were bypassed.");

    // One entity may opt into both representations without making them aliases.
    ecs.AddComponent<Transform3D>(entity2D).Position = new Vector3(9, 8, 7);
    ecs.SetPosition(entity2D, new Vector2(3, 4));

    if (ecs.GetComponent<Transform3D>(entity2D).Position != new Vector3(9, 8, 7) || ecs.AtExact(3, 4, 7) != entity2D) throw new Exception("2D and 3D transforms are coupled or layer lookup was lost.");

    ecs.AddComponent<Movement2D>(entity2D) = new Movement2D { Speed = new Vector2(2, 0), Acceleration = new Vector2(0, 4) };
    dimensions.Value.DeltaTime = 0.5f;

    var movement = ecs.AddSystem<Movement2DSystem>();
    movement.Initialize();
    movement.FixedUpdate();

    if (ecs.GetComponent<Transform2D>(entity2D).Position != new Vector2(4, 5) || ecs.AtExact(4, 5, 7) != entity2D) throw new Exception("2D integration is incorrect.");
}

var manyTriangles = new List<TraceTriangle>();

for (int i = 0; i < 2000; i++) manyTriangles.Add(new(new(i, 0, 0), new(i + 1, 0, 0), new(i, 1, 0), Vector3.One, Vector3.Zero));

var bvh = new TraceBvh(manyTriangles);
int leafTriangles = 0;

for (int i = 0; i < bvh.Nodes.Count; i++)
{
    var node = bvh.Nodes[i];

    if (node.Escape <= i || node.Escape > bvh.Nodes.Count) throw new Exception("BVH escape links do not terminate monotonically.");
    if (node.Count < 0 || node.Count > 4) throw new Exception("BVH leaf size is invalid.");

    leafTriangles += node.Count;
}

if (leafTriangles != 2000 || bvh.Nodes[0].Escape != bvh.Nodes.Count || bvh.Pack(out int offset).Length != offset + 2000 * 5) throw new Exception("BVH packing dropped geometry.");
if (new TraceBvh(Array.Empty<TraceTriangle>()).Nodes.Count != 0) throw new Exception("Empty geometry built a bogus BVH.");

// Dependencies refer to instances, including two systems of the same type, registered out of order.
using (var ordering = new DisposableScene())
{
    var calls = new List<int>();
    var first = new OrderedSystem { Calls = calls, Id = 1 };
    var second = new OrderedSystem { Calls = calls, Id = 2, After = [first] };
    ordering.Value.ECS.AddSystem(second);
    ordering.Value.ECS.AddSystem(first);
    ordering.Value.SetupECS();
    ordering.Value.ECS.Render();

    if (!calls.SequenceEqual(new[] { 1, 2 })) throw new Exception("Per-instance dependencies did not order identical system types.");
}

Console.WriteLine("PASS: compact transforms, independent dimensions, component defaults, movement, 2000-triangle BVH links/packing, and per-instance system ordering.");

using (var editing = new DisposableScene())
{
    var ecs = editing.Value.ECS;
    int entity = ecs.CreateEntity(new Vector3(1, 2, 3));
    ecs.AddComponent<Camera3D>(entity);
    ecs.AddComponent<EditorTestData>(entity) = new EditorTestData { Position = new Vector3(4, 5, 6), Count = 12, Enabled = false };

    if (!ComponentEditor.TrySetField(ecs, entity, typeof(EditorTestData), "Position", "9.25", 1, out _)) throw new Exception("Generic vector editing failed.");
    if (ecs.GetComponent<EditorTestData>(entity).Position != new Vector3(4, 9.25f, 6)) throw new Exception("Vector editing changed other axes.");

    ecs.AddComponent<Movement3D>(entity); // Changing archetypes cannot invalidate editor bindings.

    if (!ComponentEditor.TrySetField(ecs, entity, typeof(Transform3D), "Position", "17", 0, out _) || ecs.AtExact(17, 2, 3) != entity) throw new Exception("Editor changes lost spatial indexing after migration.");
    if (ComponentEditor.TrySetField(ecs, entity, typeof(Camera3D), "NearPlane", "0", 0, out _) || ecs.GetComponent<Camera3D>(entity).NearPlane != 0.1f) throw new Exception("Invalid camera editing was committed.");
    if (ComponentEditor.TrySetField(ecs, entity, typeof(EditorTestData), "Position", "NaN", 0, out _)) throw new Exception("Editor accepted a nonfinite vector.");
    if (!ComponentEditor.TrySetField(ecs, entity, typeof(EditorTestData), "Count", "2147483647", 0, out _) || ecs.GetComponent<EditorTestData>(entity).Count != int.MaxValue) throw new Exception("Integer editing lost precision.");
    if (!ComponentEditor.TrySetField(ecs, entity, typeof(EditorTestData), "Enabled", "True", 0, out _) || !ecs.GetComponent<EditorTestData>(entity).Enabled) throw new Exception("Boolean editing failed.");

    var transform = new Transform3D { Position = new Vector3(3, 4, 5), Rotation = new Vector3(0.2f, 0.7f, -0.1f), Scale = new Vector3(8) };
    var camera = new Camera3D().WithTransform(transform);

    if (camera.Position != transform.Position || Math.Abs(Vector3.Distance(camera.Position, camera.Target) - 1) > 1e-5f || Math.Abs(camera.Up.Length() - 1) > 1e-5f) throw new Exception("Camera pose incorrectly inherits transform scale.");

    var camera2D = new Camera2D { Zoom = 2 }.WithTransform(new Transform2D { Position = new Vector2(8, 9), Rotation = 0.3f, Scale = new Vector2(5) });

    if (camera2D.Position != new Vector2(8, 9) || camera2D.Zoom != 2 || camera2D.Rotation != 0.3f) throw new Exception("2D camera transform changed its lens.");

    CameraEditor.Move(ecs, entity, (-Vector3.UnitZ), Vector2.Zero, 2);

    if (ecs.GetComponent<Transform3D>(entity).Position != new Vector3(17, 2, 1)) throw new Exception("Camera navigation did not use its optional transform.");

    int direct = ecs.CreateEntity();
    ecs.AddComponent<Camera3D>(direct) = new Camera3D { Position = new Vector3(0, 0, 5), Target = Vector3.Zero };
    CameraEditor.Move(ecs, direct, (-Vector3.UnitZ), Vector2.Zero, 2);

    if (Vector3.Distance(ecs.GetComponent<Camera3D>(direct).Position, new Vector3(0, 0, 3)) > 1e-5f) throw new Exception("Direct camera navigation failed.");

    int flat = ecs.CreateEntity(new Vector2(5, 6));
    ecs.AddComponent<Camera2D>(flat);
    CameraEditor.Move(ecs, flat, new Vector3(1, 0, -1), Vector2.Zero, 2);

    if (ecs.GetComponent<Transform2D>(flat).Position != new Vector2(7, 4)) throw new Exception("2D camera navigation failed.");

    ecs.AddComponent<Camera3D>(flat);
    ecs.AddComponent<Transform3D>(flat).Position = new Vector3(10, 20, 30);
    CameraEditor.Move(ecs, flat, Vector3.UnitX, Vector2.Zero, 2, typeof(Camera2D));

    if (ecs.GetComponent<Transform2D>(flat).Position != new Vector2(9, 4) || ecs.GetComponent<Transform3D>(flat).Position != new Vector3(10, 20, 30)) throw new Exception("Mixed camera navigation changed the other dimension.");

    ecs.DestroyEntity(entity);

    if (ComponentEditor.TrySetField(ecs, entity, typeof(EditorTestData), "Count", "1", 0, out _) || ecs.TryGetComponent(entity, typeof(Camera3D), out _)) throw new Exception("Editor accessed a destroyed entity.");
}

Console.WriteLine("PASS: generic component editing, vector axes, finite values, camera validation, spatial indexing, archetype migration, destroyed entities and optional camera transforms.");

CameraControllerRegression.Run();

/// <summary>Component fixture covering the value types supported by the property editor.</summary>
struct EditorTestData : Component
{
    public Vector3 Position;
    public int Count;
    public bool Enabled;
}

/// <summary>Records render callbacks to verify system dependency ordering.</summary>
sealed class OrderedSystem : com.radiant.engine.core.System
{
    public List<int> Calls;
    public int Id;

    /// <inheritdoc/>
    public override void Render() => Calls.Add(Id);
}
/// <summary>Scene fixture that records whether disposal reaches its owned resources.</summary>
sealed class DisposableScene : IDisposable
{
    public Scene Value = new();

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public DisposableScene() => Value.Initialize();

    /// <summary>Records disposal of the scene fixture for lifecycle assertions.</summary>
    public void Dispose() => Value.Dispose();
}
