using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using PictureGeoExif.Core.Security;
using PictureGeoExif.Platform.Mac.Interop;

namespace PictureGeoExif.Platform.Mac;

/// <summary>
/// API keys in the user's login keychain (generic password, service "PictureGeoExif", account = credential target).
/// Replaces the Windows Credential Manager on macOS. Keys never appear in settings, templates, logs or process arguments.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class MacKeychainCredentialStore : ICredentialStore
{
    private const int ErrSecSuccess = 0, ErrSecItemNotFound = -25300, ErrSecDuplicateItem = -25299;
    private const string Service = "PictureGeoExif";

    [DllImport(CF.SecurityLib)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(CF.SecurityLib)] private static extern int SecItemAdd(IntPtr attributes, IntPtr result);
    [DllImport(CF.SecurityLib)] private static extern int SecItemUpdate(IntPtr query, IntPtr attributes);
    [DllImport(CF.SecurityLib)] private static extern int SecItemDelete(IntPtr query);

    public string DisplayName => "macOS-Schlüsselbund";
    public bool IsSupported => OperatingSystem.IsMacOS();

    private static IntPtr K(string name) => CF.Constant(CF.SecurityLib, name);

    private static T WithBaseQuery<T>(string target, Func<(IntPtr Key, IntPtr Value)[], T> action)
    {
        IntPtr service = CF.String(Service), account = CF.String(target);
        try
        {
            return action([(K("kSecClass"), K("kSecClassGenericPassword")), (K("kSecAttrService"), service), (K("kSecAttrAccount"), account)]);
        }
        finally { CF.Release(service); CF.Release(account); }
    }

    public string? Read(string target) => WithBaseQuery(target, entries =>
    {
        IntPtr query = CF.Dictionary([.. entries, (K("kSecReturnData"), CF.True), (K("kSecMatchLimit"), K("kSecMatchLimitOne"))]);
        try
        {
            int status = SecItemCopyMatching(query, out var data);
            if (status == ErrSecItemNotFound) return null;
            if (status != ErrSecSuccess) throw new CredentialStoreException($"Schlüsselbund-Fehler {status}.");
            try { return Encoding.UTF8.GetString(CF.Bytes(data)); }
            finally { CF.Release(data); }
        }
        finally { CF.Release(query); }
    });

    public void Write(string target, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            WithBaseQuery(target, entries =>
            {
                IntPtr value;
                fixed (byte* p = bytes) value = CF.CFDataCreate(IntPtr.Zero, p, bytes.Length);
                IntPtr add = CF.Dictionary([.. entries, (K("kSecValueData"), value)]);
                IntPtr query = CF.Dictionary(entries);
                IntPtr update = CF.Dictionary((K("kSecValueData"), value));
                try
                {
                    int status = SecItemAdd(add, IntPtr.Zero);
                    if (status == ErrSecDuplicateItem) status = SecItemUpdate(query, update);
                    if (status != ErrSecSuccess) throw new CredentialStoreException($"Schlüsselbund-Fehler {status}.");
                    return 0;
                }
                finally { CF.Release(add); CF.Release(query); CF.Release(update); CF.Release(value); }
            });
        }
        finally { Array.Clear(bytes); }
    }

    public void Delete(string target) => WithBaseQuery(target, entries =>
    {
        IntPtr query = CF.Dictionary(entries);
        try
        {
            int status = SecItemDelete(query);
            if (status is not (ErrSecSuccess or ErrSecItemNotFound)) throw new CredentialStoreException($"Schlüsselbund-Fehler {status}.");
            return 0;
        }
        finally { CF.Release(query); }
    });
}
