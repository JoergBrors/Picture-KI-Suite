namespace PictureGeoExif.Core.Platform;

/// <summary>Facts about the running platform for logging and the diagnostics window. Contains no user data.</summary>
public interface IPlatformInfo
{
    string OperatingSystem { get; }
    /// <summary>Marketing/product version, e.g. "27.0" on macOS or "10.0.26100" on Windows.</summary>
    string OperatingSystemVersion { get; }
    string ProcessArchitecture { get; }
    bool IsArm64 { get; }
    /// <summary>Path of the running .app bundle on macOS, otherwise null.</summary>
    string? AppBundlePath { get; }
    /// <summary>Folder that holds Templates/, licenses/ etc. (Contents/Resources in a bundle, otherwise the app folder).</summary>
    string ResourcesPath { get; }
}
