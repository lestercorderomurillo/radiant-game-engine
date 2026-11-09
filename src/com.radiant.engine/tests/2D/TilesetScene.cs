using com.radiant.engine.bundle;

namespace com.radiant.engine.core;

/// <summary>Demonstrates procedural terrain generation and interactive tile rendering.</summary>
public class TilesetScene : Scene
{
    private const int WorldWidth = 2056;
    private const int WorldHeight = 1024;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<PerlinNoise2D>();
        ECS.AddSystem<GizmosRenderer>();
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<WorldGen>();

        var tiles = ECS.AddSystem(new Tileset { Present = false });
        DemoRendering.AddImage(ECS, tiles, tiles.GetOutput);

        base.SetupECS();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        base.SetupScene();

        // Scene is the bridge: generate world first
        var worldGen = ECS.GetSystem<WorldGen>();
        worldGen.GenerateWorld(WorldWidth, WorldHeight);

        // Then tell Tileset to load from the generated world
        var tileset = ECS.GetSystem<Tileset>();
        tileset.LoadWorld(worldGen);
    }
}
