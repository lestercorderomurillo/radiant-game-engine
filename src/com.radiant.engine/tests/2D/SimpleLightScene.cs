using com.radiant.engine.bundle;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

/// <summary>Minimal HRC scene with a warm light centered in the fitted world.</summary>
public class SimpleLightScene : Scene
{
    /// <inheritdoc/>
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();

        var lighting = ECS.AddSystem<HRCGI>();
        DemoRendering.AddHrc(ECS, lighting);
        ECS.AddSystem<GizmosRenderer>();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        Renderer.FitWorld(Renderer.VirtualSize / 2, Renderer.VirtualSize);

        CreateCenterLight();
    }

    /// <summary>Adds the warm emissive light at the fitted world's center.</summary>
    private void CreateCenterLight()
    {
        var center = Renderer.VirtualSize / 2;
        var warmColor = new Color(255, 180, 100); // Warm orange-ish color

        LightFactory.CreateLight(ECS, center, 100f, warmColor, warmColor, 0f);
    }
}
