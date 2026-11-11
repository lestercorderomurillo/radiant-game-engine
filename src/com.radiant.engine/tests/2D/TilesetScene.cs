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
        ECS.AddSystemCollection(SystemCollections.Debugging);
        ECS.AddSystem<PerlinNoise2D>();
        ECS.AddSystem<WorldGenerator>();

        var tiles = ECS.AddSystem(new Tileset { Present = false });
        DemoRendering.AddImage(ECS, tiles, tiles.GetOutput);
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {

        // Scene is the bridge: generate world first
        var worldGen = ECS.GetSystem<WorldGenerator>();
        worldGen.GenerateWorld(WorldWidth, WorldHeight);

        // Then tell Tileset to load from the generated world
        var tileset = ECS.GetSystem<Tileset>();
        tileset.LoadWorld(worldGen);
    }
}
