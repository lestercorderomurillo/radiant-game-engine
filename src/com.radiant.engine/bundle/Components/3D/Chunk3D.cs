using com.radiant.engine.core;

namespace com.radiant.engine.bundle;

/// <summary>16x16x16 voxel chunk of 3D tiles.</summary>
[ComponentDescription("16x16x16 voxel chunk of 3D tiles.")]
public struct Chunk3D : Component
{
    public const int Size = 16;

    public Tile3D[] Tiles;

    /// <summary>Allocates tile storage for the chunk's dimensions.</summary>
    public Chunk3D()
    {
        Tiles = new Tile3D[Size * Size * Size];
    }

    /// <summary>Reads the tile stored at local chunk coordinates.</summary>
    public Tile3D Get(int x, int y, int z) => Tiles[x + y * Size + z * Size * Size];

    /// <summary>Replaces the tile stored at local chunk coordinates.</summary>
    public void Set(int x, int y, int z, Tile3D tile) => Tiles[x + y * Size + z * Size * Size] = tile;
}
