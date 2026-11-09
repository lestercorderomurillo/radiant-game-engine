using com.radiant.engine.bundle;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;

/// <summary>Two overlapping cubes exercise occlusion regardless of entity submission order.</summary>
internal sealed class Geometry3DRegressionScene : Scene
{
    internal int NearCube;
    internal int FarCube;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        var source = ECS.AddSystem<MeshGeometry3D>();
        var geometry = ECS.AddSystem(new Geometry3D { Channels = GeometryChannels.All, Source = source, After = [source] });
        ECS.AddSystem(new RenderPresentation { Input = geometry.GetOutput, ClearColor = new Color(18, 22, 30), After = [geometry] });
        base.SetupECS();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        int camera = ECS.CreateEntity();
        ECS.AddComponent<Camera3D>(camera) = new Camera3D
        {
            Position = new Vector3(0, 0, 5), Target = Vector3.Zero, FarPlane = 20
        };
        NearCube = AddCube(Vector3.Zero, Color.CornflowerBlue);
        FarCube = AddCube(new Vector3(0, 0, -2), Color.OrangeRed);
    }

    /// <summary>Adds a cube entity with the regression scene's requested transform and material.</summary>
    private int AddCube(Vector3 position, Color color)
    {
        int entity = ECS.CreateEntity(position);
        ECS.GetComponent<Transform3D>(entity).Scale = Vector3.One;
        ECS.AddComponent<Mesh3D>(entity) = new Mesh3D { Color = color };

        return entity;
    }
}
