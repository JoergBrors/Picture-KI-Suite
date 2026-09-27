using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace PictureGeoExif.Platform.Mac.Interop;

/// <summary>CoreFoundation, ImageIO and Security C APIs (no Objective-C messaging needed).</summary>
[SupportedOSPlatform("macos")]
internal static unsafe class CF
{
    public const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const string ImageIOLib = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    public const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    public const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";

    private const int kCFNumberSInt32Type = 3, kCFNumberFloat64Type = 6;
    private const uint kCFStringEncodingUTF8 = 0x08000100;

    [DllImport(CoreFoundationLib)] public static extern void CFRelease(IntPtr value);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFStringCreateWithBytes(IntPtr allocator, byte* bytes, nint length, uint encoding, byte isExternal);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFNumberCreate(IntPtr allocator, nint type, void* value);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr* keys, IntPtr* values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CoreFoundationLib)] public static extern IntPtr CFDataCreate(IntPtr allocator, byte* bytes, nint length);
    [DllImport(CoreFoundationLib)] public static extern IntPtr CFDataCreateMutable(IntPtr allocator, nint capacity);
    [DllImport(CoreFoundationLib)] public static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundationLib)] public static extern byte* CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundationLib)] private static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte* path, nint length, byte isDirectory);

    private static readonly IntPtr CoreFoundation = NativeLibrary.Load(CoreFoundationLib);
    private static readonly IntPtr KeyCallbacks = NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryKeyCallBacks");
    private static readonly IntPtr ValueCallbacks = NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryValueCallBacks");
    public static readonly IntPtr True = Constant(CoreFoundationLib, "kCFBooleanTrue");

    /// <summary>Reads an exported CF constant (the symbol holds a pointer to the object).</summary>
    public static IntPtr Constant(string library, string symbol) => Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(library), symbol));

    /// <summary>+1 CFString; release with <see cref="CFRelease"/>.</summary>
    public static IntPtr String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        fixed (byte* p = bytes) return CFStringCreateWithBytes(IntPtr.Zero, p, bytes.Length, kCFStringEncodingUTF8, 0);
    }

    public static IntPtr Int32(int value) => CFNumberCreate(IntPtr.Zero, kCFNumberSInt32Type, &value);
    public static IntPtr Double(double value) => CFNumberCreate(IntPtr.Zero, kCFNumberFloat64Type, &value);

    /// <summary>+1 immutable CFDictionary (keys and values are retained by the dictionary).</summary>
    public static IntPtr Dictionary(params (IntPtr Key, IntPtr Value)[] entries)
    {
        var keys = entries.Select(e => e.Key).ToArray();
        var values = entries.Select(e => e.Value).ToArray();
        fixed (IntPtr* k = keys) fixed (IntPtr* v = values)
            return CFDictionaryCreate(IntPtr.Zero, k, v, entries.Length, KeyCallbacks, ValueCallbacks);
    }

    public static IntPtr FileUrl(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        fixed (byte* p = bytes) return CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, p, bytes.Length, 0);
    }

    public static byte[] Bytes(IntPtr data)
    {
        nint length = CFDataGetLength(data);
        if (length <= 0) return [];
        return new ReadOnlySpan<byte>(CFDataGetBytePtr(data), checked((int)length)).ToArray();
    }

    public static void Release(IntPtr value) { if (value != IntPtr.Zero) CFRelease(value); }
}
