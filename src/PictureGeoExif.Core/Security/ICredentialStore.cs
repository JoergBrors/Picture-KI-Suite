namespace PictureGeoExif.Core.Security;

/// <summary>
/// OS secret storage for API keys (Windows Credential Manager, macOS Keychain). Keys never live in templates, settings or logs.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Human-readable name of the backing store for UI texts.</summary>
    string DisplayName { get; }
    bool IsSupported { get; }
    string? Read(string target);
    void Write(string target, string secret);
    void Delete(string target);
}

/// <summary>Fallback without secure storage: keys come only from environment variables.</summary>
public sealed class NoCredentialStore : ICredentialStore
{
    public string DisplayName => "keine sichere Ablage (nur Umgebungsvariablen)";
    public bool IsSupported => false;
    public string? Read(string target) => null;
    public void Write(string target, string secret) => throw new PlatformNotSupportedException("Auf dieser Plattform ist keine sichere Schlüsselablage verfügbar. Bitte Umgebungsvariable setzen.");
    public void Delete(string target) { }
}

public sealed class CredentialStoreException(string message, Exception? inner = null) : Exception(message, inner);
