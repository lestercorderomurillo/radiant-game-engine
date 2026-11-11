using System.Numerics;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.math;
using com.radiant.engine.runtime;

public class TriangleScene : Scene
{
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        var light = ECS.AddSystem<HRCGI>();
        var color = ECS.AddSystem(new ToneMapping
        {
            Input = light.GetOutput, Exposure = 2f,
            Mapper = ToneMapper.None, After = [light]
        });
        ECS.AddSystem(new RenderPresentation { Input = color.GetOutput, After = [color] });
    }

    public override void SetupScene()
    {
        var center = Renderer.VirtualSize / 2;
        Renderer.FitWorld(center, Renderer.VirtualSize);
        int triangle = ECS.CreateEntity();
        ECS.AddComponent<Transform2D>(triangle) = new()
        {
            Position = center - new Vector2(180), Scale = Vector2.One
        };
        ECS.AddComponent<Triangle2D>(triangle) = new() { Size = new Vector2(360), Bordered = true };
        ECS.AddComponent<Material>(triangle) = new()
        {
            Albedo = Color.Black, Emissive = new Color(220, 80, 255)
        };
        LightFactory.CreateLight(ECS, center + new Vector2(-400, 100), 50, new Color(0, 220, 255), new Color(0, 220, 255));
        LightFactory.CreateLight(ECS, center + new Vector2(400, 100), 50, Color.Orange, Color.Orange);
    }
}
