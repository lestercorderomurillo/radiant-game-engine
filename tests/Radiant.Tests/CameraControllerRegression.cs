using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using com.radiant.engine.bundle;
using com.radiant.engine.core;

internal static class CameraControllerRegression
{
    private sealed class TestCameraView : com.radiant.engine.core.System, ICameraView
    {
        public Type CameraType { get; set; }
        public int CameraEntity { get; set; }
    }

    public static void Run()
    {
        using var fixture = new DisposableScene();
        var scene = fixture.Value;
        // Controller selection can be tested without creating GPU resources or a desktop window.
        var inspector = new Inspector { Scene = scene, Renderer = (Renderer)RuntimeHelpers.GetUninitializedObject(typeof(Renderer)) };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(Inspector).GetField(name, flags)!.SetValue(inspector, value);
        T Get<T>(string name) => (T)typeof(Inspector).GetField(name, flags)!.GetValue(inspector)!;
        void Refresh() => typeof(Inspector).GetMethod("RefreshCameraControls", flags)!.Invoke(inspector, null);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        try
        {
            Refresh();
            Check(Get<int>("ControlledCamera") == -1, "Empty scenes must not select an entity.");
            int flat = scene.ECS.CreateEntity();
            scene.ECS.AddComponent<Camera2D>(flat);
            int spatial = scene.ECS.CreateEntity();
            scene.ECS.AddComponent<Camera3D>(spatial);
            var firstView = scene.ECS.AddSystem(new TestCameraView { CameraType = typeof(Camera2D), CameraEntity = flat });
            var secondView = scene.ECS.AddSystem(new TestCameraView { CameraType = typeof(Camera2D), CameraEntity = flat });
            scene.ECS.AddSystem(new TestCameraView { CameraType = typeof(Camera3D), CameraEntity = spatial });
            Refresh();
            Check(Get<ICameraView[]>("CameraViews").SequenceEqual(new ICameraView[] { firstView, secondView }), "Compatible view instances must remain individually selectable.");
            Set("CameraViewIndex", 1);
            Refresh();
            Check(Get<int>("CameraViewIndex") == 1, "Refreshing must preserve the selected render view.");
            Check(Get<int[]>("CameraEntities").SequenceEqual(new[] { flat, spatial }), "Controller must discover both camera dimensions.");
            Set("ControlledCamera", spatial);
            Set("InspectedEntity", flat);
            Refresh();
            Check(Get<int>("ControlledCamera") == spatial, "Property inspection must not change the controlled camera.");
            Set("CameraSearch", "camera2d");
            Refresh();
            Check(Get<int>("ControlledCamera") == flat, "Camera search must filter case-insensitively.");
            Set("CameraSearch", "no match");
            Set("NavigatingCamera", true);
            Refresh();
            Check(Get<int>("ControlledCamera") == -1 && !Get<bool>("NavigatingCamera"), "Empty results must stop navigation.");
            Set("CameraSearch", "");
            Set("ControlledCamera", spatial);
            scene.ECS.DestroyEntity(spatial);
            Refresh();
            Check(Get<int>("ControlledCamera") == flat, "Deleted cameras must not leave stale selection.");
            scene.ECS.DisableEntity(flat);
            Set("NavigatingCamera", true);
            Refresh();
            Check(!Get<bool>("NavigatingCamera"), "Disabled cameras must stop navigation.");
        }
        finally
        {
            typeof(Inspector).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);
        }
        Console.WriteLine("PASS: shared camera discovery, independent selection, search, empty results, deletion and disabled entities.");
    }
}
