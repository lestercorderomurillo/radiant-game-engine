using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.bundle;

internal sealed class OidnNative
{
    private static OidnNative Loaded;
    public static string LoadError { get; private set; } = "Install the optional OIDN runtime with python3 eng/install-oidn.py and rebuild.";
    private readonly IntPtr Library;
    private OidnNative(IntPtr library) { Library = library; }
    public static bool TryGet(out OidnNative api)
    {
        if (Loaded != null) { api = Loaded; return true; }
        string name = OperatingSystem.IsWindows() ? "OpenImageDenoise.dll" : OperatingSystem.IsMacOS() ? "libOpenImageDenoise.2.dylib" : "libOpenImageDenoise.so.2";
        string configured = Environment.GetEnvironmentVariable("RADIANT_OIDN_LIBRARY");
        foreach (string candidate in new[] { configured, Path.Combine(AppContext.BaseDirectory, "Native", "oidn", "lib", name), Path.Combine(AppContext.BaseDirectory, "Native", "oidn", "bin", name), name })
        {
            if (string.IsNullOrWhiteSpace(candidate) || !NativeLibrary.TryLoad(candidate, out var handle)) continue;
            Loaded = new OidnNative(handle); api = Loaded; return true;
        }
        api = null; return false;
    }
    private T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountDevices();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PhysicalInt(int id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PhysicalString(int id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewDevice(int id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void HandleAction(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetInt(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBool(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.I1)] bool value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewFilter(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewBuffer(IntPtr device, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BufferCopy(IntPtr buffer, nuint offset, nuint bytes, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FilterImage(IntPtr filter, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr buffer, int format, nuint width, nuint height, nuint offset, nuint pixelStride, nuint rowStride);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceError(IntPtr device, out IntPtr message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool ProgressMonitor(IntPtr user, double progress);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetMonitor(IntPtr filter, ProgressMonitor callback, IntPtr user);
    public sealed class Session : IDisposable
    {
        private readonly OidnNative Api;
        private IntPtr Device, Filter;
        private readonly IntPtr[] Buffers = new IntPtr[4];
        private readonly HandleAction Execute;
        private readonly BufferCopy Write, Read;
        private readonly Vector3[] Result;
        private readonly ProgressMonitor Monitor;
        private volatile Func<bool> Cancelled;
        public string DeviceName { get; }
        public int Width { get; }
        public int Height { get; }
        public OidnQuality Quality { get; }
        public OidnDevice Preference { get; }
        public Session(OidnNative api, int width, int height, OidnQuality quality, OidnDevice preference)
        {
            Monitor = (_, _) => Cancelled?.Invoke() != true;
            Api = api; Width = width; Height = height; Result = new Vector3[checked(width * height)]; Quality = quality; Preference = preference;
            Execute = api.Function<HandleAction>("oidnExecuteFilter"); Write = api.Function<BufferCopy>("oidnWriteBuffer"); Read = api.Function<BufferCopy>("oidnReadBuffer");
            try
            {
                int count = api.Function<CountDevices>("oidnGetNumPhysicalDevices")(), selected = -1, type = 0;
                var physical = api.Function<PhysicalInt>("oidnGetPhysicalDeviceInt");
                for (int i = 0; i < count; i++)
                {
                    int candidate = physical(i, "type");
                    if (preference == OidnDevice.CPU && candidate != 1 || preference == OidnDevice.GPU && candidate == 1) continue;
                    selected = i; type = candidate; break;
                }
                if (selected < 0) throw new NotSupportedException($"OIDN found no supported {preference} device.");
                DeviceName = Marshal.PtrToStringUTF8(api.Function<PhysicalString>("oidnGetPhysicalDeviceString")(selected, "name")) ?? preference.ToString();
                Device = api.Function<NewDevice>("oidnNewDeviceByID")(selected); Check();
                if (type == 1) api.Function<SetInt>("oidnSetDeviceInt")(Device, "numThreads", Math.Max(1, Environment.ProcessorCount / 2));
                api.Function<HandleAction>("oidnCommitDevice")(Device); Check();
                Filter = api.Function<NewFilter>("oidnNewFilter")(Device, "RT"); Check();
                var createBuffer = api.Function<NewBuffer>("oidnNewBuffer"); var image = api.Function<FilterImage>("oidnSetFilterImage");
                string[] names = ["color", "albedo", "normal", "output"];
                for (int i = 0; i < Buffers.Length; i++)
                {
                    Buffers[i] = createBuffer(Device, checked((nuint)(width * height * 12))); Check();
                    image(Filter, names[i], Buffers[i], 3, (nuint)width, (nuint)height, 0, 12, (nuint)(width * 12));
                }
                api.Function<SetMonitor>("oidnSetFilterProgressMonitorFunction")(Filter, Monitor, IntPtr.Zero);
                var boolean = api.Function<SetBool>("oidnSetFilterBool");
                boolean(Filter, "hdr", true); boolean(Filter, "cleanAux", true);
                api.Function<SetInt>("oidnSetFilterInt")(Filter, "quality", quality == OidnQuality.Fast ? 4 : quality == OidnQuality.Balanced ? 5 : 6);
                api.Function<HandleAction>("oidnCommitFilter")(Filter); Check();
            }
            catch { Dispose(); throw; }
        }
        public unsafe Vector3[] Denoise(Vector3[] color, Vector3[] albedo, Vector3[] normals, Func<bool> cancelled)
        {
            Cancelled = cancelled;
            if (cancelled()) throw new OperationCanceledException();
            nuint bytes = checked((nuint)(Width * Height * 12));
            fixed (Vector3* c = color) fixed (Vector3* a = albedo) fixed (Vector3* n = normals)
            {
                Write(Buffers[0], 0, bytes, (IntPtr)c); Write(Buffers[1], 0, bytes, (IntPtr)a); Write(Buffers[2], 0, bytes, (IntPtr)n);
            }
            Execute(Filter); Check();
            var result = Result;
            fixed (Vector3* data = result) Read(Buffers[3], 0, bytes, (IntPtr)data);
            Check();
            if (cancelled()) throw new OperationCanceledException();
            Cancelled = null; return result;
        }
        private void Check()
        {
            int code = Api.Function<DeviceError>("oidnGetDeviceError")(Device, out var message);
            if (code == 6 && Cancelled?.Invoke() == true) throw new OperationCanceledException();
            if (code != 0) throw new InvalidOperationException($"OIDN: {Marshal.PtrToStringUTF8(message) ?? code.ToString()}");
        }
        public void Dispose()
        {
            if (Filter != IntPtr.Zero) { Api.Function<HandleAction>("oidnReleaseFilter")(Filter); Filter = IntPtr.Zero; }
            for (int i = 0; i < Buffers.Length; i++) if (Buffers[i] != IntPtr.Zero) { Api.Function<HandleAction>("oidnReleaseBuffer")(Buffers[i]); Buffers[i] = IntPtr.Zero; }
            if (Device != IntPtr.Zero) { Api.Function<HandleAction>("oidnReleaseDevice")(Device); Device = IntPtr.Zero; }
        }
    }
}
