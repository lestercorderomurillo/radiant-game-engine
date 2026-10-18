using System;
using System.Collections.Generic;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

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
if (Scene.ECS.GetComponent<Circle2D>(Light).Radius != 100 || Scene.ECS.GetComponent<Transform>(Light).Position.X != 1920)
    throw new Exception("Light components were lost during archetype migration.");
Scene.Dispose();
var Loop = new GameLoop();
Loop.Initialize();
Loop.Dispose();
Console.WriteLine("PASS: 2048 populated tiles, spatial lookups, light creation, platform-safe timer lifecycle.");
