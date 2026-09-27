using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using PictureGeoExif.Core.Security;

namespace PictureGeoExif.Platform.Windows;

/// <summary>Windows Credential Manager (generic credentials, current user). Keys never live in templates or settings.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    public string DisplayName => "Windows-Anmeldeinformationen";
    public bool IsSupported => OperatingSystem.IsWindows();

    public string? Read(string target)
    {
        try { return ReadCore(target); }
        catch (Win32Exception ex) { throw new CredentialStoreException(ex.Message, ex); }
    }

    public void Write(string target, string secret)
    {
        try { WriteCore(target, secret); }
        catch (Win32Exception ex) { throw new CredentialStoreException(ex.Message, ex); }
    }

    public void Delete(string target)
    {
        try { DeleteCore(target); }
        catch (Win32Exception ex) { throw new CredentialStoreException(ex.Message, ex); }
    }

    private const int GenericType = 1, PersistLocalMachine = 2, NotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    private static string? ReadCore(string target)
    {
        if (!CredRead(target, GenericType, 0, out var pointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == NotFound ? null : throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally { CredFree(pointer); }
    }

    private static void WriteCore(string target, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = GenericType, TargetName = target, CredentialBlob = blob, CredentialBlobSize = bytes.Length,
                Persist = PersistLocalMachine, UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
        }
    }

    private static void DeleteCore(string target)
    {
        if (!CredDelete(target, GenericType, 0) && Marshal.GetLastWin32Error() is var error && error != NotFound)
            throw new Win32Exception(error);
    }
}
