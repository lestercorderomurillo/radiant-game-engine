using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class SimpleLightScene : Scene
{
    private HRCGI HRCGISystem;
    private Bilinear BilinearSystem;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();
        HRCGISystem = ECS.AddSystem<HRCGI>();
        BilinearSystem = ECS.AddSystem<Bilinear>();
        ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        CreateCenterLight();
        BilinearSystem.SetInputSource(HRCGISystem.GetOutput);

        base.SetupScene();
    }

    private void CreateCenterLight()
    {
        var center = Renderer.VirtualSize / 2;
        var warmColor = new Color(255, 180, 100); // Warm orange-ish color

        LightFactory.CreateLight(ECS, center, 100f, warmColor, warmColor, 0f);
    }
}
