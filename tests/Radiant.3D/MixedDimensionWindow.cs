using Window = com.radiant.engine.backends.monogame.MonoGameWindow;
using System;
using com.radiant.engine.bundle;
using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/// <summary>One entity, independent 2D/3D data, two instances of the same AA and presentation systems.</summary>
internal sealed class MixedDimensionScene : Scene
{
    internal Geometry3D MeshView;
    internal ShapeGeometry2D ShapeView;

    /// <inheritdoc/>
    public override void SetupECS()
    {
        var source = ECS.AddSystem(new MeshGeometry3D { Resolution = new Point(320, 360) });
        MeshView = ECS.AddSystem(new Geometry3D { Source = source, After = [source] });
        ShapeView = ECS.AddSystem(new ShapeGeometry2D { Resolution = new Point(320, 360) });

        var aa3D = ECS.AddSystem(new AntiAliasing { Input = MeshView.GetOutput, After = [MeshView] });
        var aa2D = ECS.AddSystem(new AntiAliasing { Input = ShapeView.GetOutput, After = [ShapeView] });
        var left = ECS.AddSystem(new RenderPresentation { Input = aa3D.GetOutput, Destination = new Rectangle(0, 0, 320, 360), ClearColor = Color.Black, After = [aa3D] });
        ECS.AddSystem(new RenderPresentation { Input = aa2D.GetOutput, Destination = new Rectangle(320, 0, 320, 360), After = [left, aa2D] });
        base.SetupECS();
    }

    /// <inheritdoc/>
    public override void SetupScene()
    {
        Renderer.FitWorld(new Vector2(160, 90), new Vector2(320, 180));

        int camera = ECS.CreateEntity();
        ECS.AddComponent<Camera3D>(camera) = new Camera3D { Position = new Vector3(0, 0, 5), Target = Vector3.Zero };

        int entity = ECS.CreateEntity(new Vector2(160, 90));
        ECS.AddComponent<Transform3D>(entity) = new Transform3D();
        ECS.AddComponent<Mesh3D>(entity) = new Mesh3D { Color = Color.CornflowerBlue };
        ECS.AddComponent<Circle2D>(entity) = new Circle2D { Radius = 30 };
        ECS.AddComponent<Material>(entity) = new Material { Albedo = Color.Coral };
    }
}
/// <summary>Runs mixed dimension checks and records diagnostic frames.</summary>
internal sealed class MixedDimensionWindow : Window
{
    private readonly MixedDimensionScene Scene = new();
    private int Frame;

    /// <summary>Creates the test fixture with the supplied scene or window configuration.</summary>
    public MixedDimensionWindow() : base(new GameLoop(), new() { Title = "Radiant mixed-dimension regression", Width = 640, Height = 360 })
    {
        GameLoop.AddScene(Scene);
        GameLoop.ThrottleUnfocused = false;
    }

    /// <inheritdoc/>
    protected override void Draw(GameTime time)
    {
        base.Draw(time);

        if (++Frame != 10) return;

        var target = Scene.Renderer.SceneRT;
        var pixels = new Color[target.Width * target.Height];
        target.GetData(pixels);

        if (pixels[180 * 640 + 160] != Color.CornflowerBlue || pixels[180 * 640 + 480] != Color.Coral) throw new Exception("Independent 2D/3D views did not composite their own outputs.");
        if (Scene.MeshView.NormalTexture != null || Scene.MeshView.DepthTexture != null) throw new Exception("Flat mesh view allocated unrequested buffers.");
        if (Scene.ShapeView.ColorTexture.Width != 320 || Scene.MeshView.ColorTexture.Height != 360) throw new Exception("Independent view resolution was ignored.");

        Console.WriteLine("PASS: one entity rendered in independent 2D/3D views, repeated AA/presentation types, per-view resolution and color-only allocation.");
        Exit();
    }
}
