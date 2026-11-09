using System;
using System.Linq;
using com.radiant.engine.core;
using com.radiant.engine.runtime;

RuntimeBackendRegression.Run();
InputRegression.Run();
ResourceCacheRegression.Run();
FontServiceRegression.Run();
ShapeCollectionRegression.Run();
PortableRendererRegression.Run();

bool Native(string name) => name == "radiant" || name.Contains("MonoGame", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("FontStash", StringComparison.OrdinalIgnoreCase) || name.Contains("Xna", StringComparison.OrdinalIgnoreCase);
if (typeof(Renderer).Assembly.GetReferencedAssemblies().Any(a => Native(a.Name)))
    throw new Exception("Core assembly references a native graphics framework.");
if (AppDomain.CurrentDomain.GetAssemblies().Any(a => Native(a.GetName().Name)))
    throw new Exception("Portable renderer execution loaded a native graphics framework.");
RuntimeBackends.DefaultFactory = () => new HeadlessBackend(1, TimeSpan.FromSeconds(1.0 / 60));
try { new GameClient().Run(new Scene()); }
finally { RuntimeBackends.DefaultFactory = null; }
Console.WriteLine("PASS: core-only application, ECS/input and actual renderer run without MonoGame or FontStash assemblies; default backend is replaceable.");
