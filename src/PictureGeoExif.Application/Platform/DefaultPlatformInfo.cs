using System.Runtime.InteropServices;
using PictureGeoExif.Core.Platform;

namespace PictureGeoExif.Application.Platform;

/// <summary>Platform facts from the .NET runtime; used on Windows and Linux, and as base for macOS.</summary>
public class DefaultPlatformInfo : IPlatformInfo
{
    public virtual string OperatingSystem =>
        System.OperatingSystem.IsWindows() ? "Windows" : System.OperatingSystem.IsMacOS() ? "macOS" : System.OperatingSystem.IsLinux() ? "Linux" : RuntimeInformation.OSDescription;
    public virtual string OperatingSystemVersion => Environment.OSVersion.Version.ToString();
    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
    public bool IsArm64 => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    public virtual string? AppBundlePath => null;
    public virtual string ResourcesPath => AppContext.BaseDirectory;
}
