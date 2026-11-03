using System;

namespace com.radiant.engine.core;

/// <summary>A view's camera binding, exposed to generic editor tools.</summary>
public interface ICameraView
{
    Type CameraType { get; }
    int CameraEntity { get; set; }
}
