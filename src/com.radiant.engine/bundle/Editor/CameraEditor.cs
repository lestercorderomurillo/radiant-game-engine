using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using System;

namespace com.radiant.engine.bundle;

/// <summary>Editor navigation in camera-local axes, independent of input devices and rendering views.</summary>
public static class CameraEditor
{
    public static void Move(ECS ecs, int entity, Vector3 movement, Vector2 look, float distance, Type cameraType = null)
    {
        if (!ecs.IsAlive(entity) || ecs.IsDisabled(entity)) return;
        cameraType ??= ecs.HasComponent<Camera3D>(entity) ? typeof(Camera3D) : typeof(Camera2D);
        if (cameraType == typeof(Camera3D) && ecs.HasComponent<Camera3D>(entity))
        {
            ref var lens = ref ecs.GetComponent<Camera3D>(entity);
            bool transformed = ecs.HasComponent<Transform3D>(entity);
            var camera = transformed ? lens.WithTransform(ecs.GetComponent<Transform3D>(entity)) : lens;
            var forward = Vector3.Normalize(camera.Target - camera.Position);
            float pitch = MathHelper.Clamp(MathF.Asin(MathHelper.Clamp(forward.Y, -1, 1)) - look.Y, -1.55f, 1.55f);
            float yaw = MathF.Atan2(-forward.X, -forward.Z) - look.X;
            var rotation = Matrix.CreateFromYawPitchRoll(yaw, pitch, 0);
            var offset = Vector3.TransformNormal(movement, rotation) * distance;
            if (transformed)
            {
                var transform = ecs.GetComponent<Transform3D>(entity);
                transform.Position += offset; transform.Rotation = new Vector3(pitch, yaw, 0);
                ecs.TrySetComponent(entity, transform);
            }
            else
            {
                camera.Position += offset;
                camera.Target = camera.Position + Vector3.TransformNormal(Vector3.Forward, rotation);
                camera.Up = Vector3.TransformNormal(Vector3.Up, rotation);
                lens = camera;
            }
        }
        else if (cameraType == typeof(Camera2D) && ecs.HasComponent<Camera2D>(entity))
        {
            var camera = ecs.GetComponent<Camera2D>(entity);
            var offset = new Vector2(movement.X, movement.Z) * distance;
            if (ecs.HasComponent<Transform2D>(entity))
            {
                var transform = ecs.GetComponent<Transform2D>(entity);
                transform.Position += offset; transform.Rotation -= look.X;
                ecs.TrySetComponent(entity, transform);
            }
            else { camera.Position += offset; camera.Rotation -= look.X; ecs.GetComponent<Camera2D>(entity) = camera; }
        }
    }
}
