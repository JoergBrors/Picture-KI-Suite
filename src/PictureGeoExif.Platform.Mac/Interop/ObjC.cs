using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PictureGeoExif.Platform.Mac.Interop;

/// <summary>
/// Thin access to the Objective-C runtime. Every message send goes through typed function pointers to objc_msgSend
/// (arm64 and x86_64 use objc_msgSend for all signatures used here: no stret, small HFA structs return in registers).
/// Callers must hold an <see cref="AutoreleasePool"/> while working with returned objects.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(LibObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(LibObjC)] public static extern IntPtr objc_retain(IntPtr value);
    [DllImport(LibObjC)] public static extern void objc_release(IntPtr value);

    private static readonly IntPtr MsgSend = NativeLibrary.GetExport(NativeLibrary.Load(LibObjC), "objc_msgSend");
    private static readonly ConcurrentDictionary<string, IntPtr> Selectors = new();
    private static readonly ConcurrentDictionary<string, IntPtr> Classes = new();

    public static IntPtr Sel(string name) => Selectors.GetOrAdd(name, sel_registerName);

    public static IntPtr Class(string name)
    {
        var cls = Classes.GetOrAdd(name, objc_getClass);
        if (cls == IntPtr.Zero) { Classes.TryRemove(name, out _); throw new EntryPointNotFoundException("Objective-C-Klasse nicht gefunden: " + name); }
        return cls;
    }

    public static bool HasClass(string name) => objc_getClass(name) != IntPtr.Zero;

    /// <summary>Loads a system framework by path (e.g. Photos). Returns false if it is not present.</summary>
    public static bool LoadFramework(string name) =>
        NativeLibrary.TryLoad($"/System/Library/Frameworks/{name}.framework/{name}", out _);

    // --- message sends (object results) ---
    public static IntPtr Send(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector));
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector), a);
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a, IntPtr b) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector), a, b);
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a, IntPtr b, IntPtr c) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector), a, b, c);
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a, IntPtr b, IntPtr c, IntPtr d) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector), a, b, c, d);
    public static IntPtr SendN(IntPtr receiver, string selector, nint a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, IntPtr>)MsgSend)(receiver, Sel(selector), a);
    public static IntPtr SendN(IntPtr receiver, string selector, nint a, IntPtr b) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector), a, b);
    public static IntPtr SendN(IntPtr receiver, string selector, nint a, nint b, IntPtr c) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, nint, IntPtr, IntPtr>)MsgSend)(receiver, Sel(selector), a, b, c);
    public static IntPtr SendBool(IntPtr receiver, string selector, IntPtr a, byte b) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte, IntPtr>)MsgSend)(receiver, Sel(selector), a, b);

    // --- message sends (void, setters) ---
    public static void SendVoid(IntPtr receiver, string selector, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)MsgSend)(receiver, Sel(selector), a);
    public static void SendVoidByte(IntPtr receiver, string selector, byte a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte, void>)MsgSend)(receiver, Sel(selector), a);
    public static void SendVoidN(IntPtr receiver, string selector, nint a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, void>)MsgSend)(receiver, Sel(selector), a);
    public static void SendVoidInt(IntPtr receiver, string selector, int a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, int, void>)MsgSend)(receiver, Sel(selector), a);

    // --- message sends (scalar results) ---
    public static nint SendNint(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint>)MsgSend)(receiver, Sel(selector));
    public static nint SendNint(IntPtr receiver, string selector, nint a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, nint>)MsgSend)(receiver, Sel(selector), a);
    public static nuint SendNuint(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint>)MsgSend)(receiver, Sel(selector));
    public static byte SendByte(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte>)MsgSend)(receiver, Sel(selector));
    public static double SendDouble(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double>)MsgSend)(receiver, Sel(selector));
    public static int SendInt(IntPtr receiver, string selector, IntPtr a, IntPtr b, IntPtr c, IntPtr d) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)MsgSend)(receiver, Sel(selector), a, b, c, d);
    public static CoordinatePair SendCoordinate(IntPtr receiver, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CoordinatePair>)MsgSend)(receiver, Sel(selector));
    public static int SendImageRequest(IntPtr receiver, IntPtr asset, SizePair size, nint contentMode, IntPtr options, IntPtr block) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, SizePair, nint, IntPtr, IntPtr, int>)MsgSend)(
            receiver, Sel("requestImageForAsset:targetSize:contentMode:options:resultHandler:"), asset, size, contentMode, options, block);
    public static IntPtr SendRepresentation(IntPtr receiver, nuint type, IntPtr properties) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, IntPtr, IntPtr>)MsgSend)(receiver, Sel("representationUsingType:properties:"), type, properties);

    // --- Foundation helpers ---
    public static IntPtr NSString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* p = bytes) return Send(Class("NSString"), "stringWithUTF8String:", (IntPtr)p);
    }

    public static string? FromNSString(IntPtr value) =>
        value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(value, "UTF8String"));

    public static IntPtr NSArrayWith(IntPtr item) => Send(Class("NSArray"), "arrayWithObject:", item);

    /// <summary>NSDate → seconds since 1970, or null.</summary>
    public static double? UnixTime(IntPtr date) => date == IntPtr.Zero ? null : SendDouble(date, "timeIntervalSince1970");

    public static byte[] NSDataBytes(IntPtr data)
    {
        if (data == IntPtr.Zero) return [];
        nuint length = SendNuint(data, "length");
        if (length == 0) return [];
        var result = new byte[checked((int)length)];
        Marshal.Copy(Send(data, "bytes"), result, 0, result.Length);
        return result;
    }

    public static (string? Domain, long Code, string? Message) ErrorInfo(IntPtr error) =>
        error == IntPtr.Zero ? (null, 0, null)
            : (FromNSString(Send(error, "domain")), SendNint(error, "code"), FromNSString(Send(error, "localizedDescription")));

    /// <summary>alloc + init; the caller owns the result and must <see cref="objc_release"/> it.</summary>
    public static IntPtr New(string className) => Send(Send(Class(className), "alloc"), "init");

    public static IntPtr PushPool() => objc_autoreleasePoolPush();
    public static void PopPool(IntPtr pool) => objc_autoreleasePoolPop(pool);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CoordinatePair(double first, double second)
{
    public readonly double First = first;
    public readonly double Second = second;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SizePair(double width, double height)
{
    public readonly double Width = width;
    public readonly double Height = height;
}

/// <summary>Scope for objects returned autoreleased by Objective-C APIs.</summary>
[SupportedOSPlatform("macos")]
internal readonly struct AutoreleasePool : IDisposable
{
    private readonly IntPtr pool;
    private AutoreleasePool(IntPtr pool) => this.pool = pool;
    public static AutoreleasePool Create() => new(ObjC.PushPool());
    public void Dispose() { if (pool != IntPtr.Zero) ObjC.PopPool(pool); }
}
