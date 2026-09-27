using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PictureGeoExif.Core.Platform;

namespace PictureGeoExif.Platform.Mac;

/// <summary>macOS facts: product version via sysctl, .app bundle and Contents/Resources detection.</summary>
public sealed class MacPlatformInfo : IPlatformInfo
{
    public string OperatingSystem => "macOS";

    public string OperatingSystemVersion => System.OperatingSystem.IsMacOS() ? ProductVersion() ?? Environment.OSVersion.Version.ToString() : Environment.OSVersion.Version.ToString();

    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
    public bool IsArm64 => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>Folder ending in ".app" when the executable runs from &lt;Name&gt;.app/Contents/MacOS.</summary>
    public string? AppBundlePath => FindBundle(AppContext.BaseDirectory);

    public string ResourcesPath
    {
        get
        {
            if (AppBundlePath is { } bundle && Directory.Exists(Path.Combine(bundle, "Contents", "Resources")))
                return Path.Combine(bundle, "Contents", "Resources");
            return AppContext.BaseDirectory;
        }
    }

    /// <summary>Pure string logic (no file system access), so it behaves the same on every OS and in tests.</summary>
    public static string? FindBundle(string baseDirectory)
    {
        string path = baseDirectory.TrimEnd('/', '\\');
        foreach (var suffix in new[] { "/Contents/MacOS", "\\Contents\\MacOS" })
        {
            if (!path.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string bundle = path[..^suffix.Length];
            return bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
        }
        return null;
    }

    [SupportedOSPlatform("macos")]
    private static unsafe string? ProductVersion()
    {
        try
        {
            nuint size = 0;
            if (sysctlbyname("kern.osproductversion", null, &size, null, 0) != 0 || size == 0 || size > 256) return null;
            var buffer = stackalloc byte[(int)size];
            if (sysctlbyname("kern.osproductversion", buffer, &size, null, 0) != 0) return null;
            return Marshal.PtrToStringUTF8((IntPtr)buffer);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern unsafe int sysctlbyname(string name, void* oldp, nuint* oldlenp, void* newp, nuint newlen);
}
