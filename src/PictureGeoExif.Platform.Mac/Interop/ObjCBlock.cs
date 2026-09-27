using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PictureGeoExif.Platform.Mac.Interop;

/// <summary>
/// Minimal Objective-C block support (Clang block ABI). A block is created as a "stack block" in native memory:
/// if the callee keeps it, it calls Block_copy, which copies the literal (including our context id) to the heap
/// and manages that copy itself. Our literal therefore only has to live until the Objective-C call returns.
/// The context is an id into a registry, never a raw GCHandle, so a late or repeated callback can never touch freed memory.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe class ObjCBlock
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Literal
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public Descriptor* Descriptor;
        public long Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Descriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    private static readonly IntPtr StackBlockIsa = ResolveIsa();
    private static readonly Descriptor* SharedDescriptor = CreateDescriptor();
    private static readonly ConcurrentDictionary<long, object> Registry = new();
    private static long nextId;

    private static IntPtr ResolveIsa()
    {
        foreach (var library in new[] { "/usr/lib/libSystem.B.dylib", "/usr/lib/system/libsystem_blocks.dylib" })
            if (NativeLibrary.TryLoad(library, out var handle) && NativeLibrary.TryGetExport(handle, "_NSConcreteStackBlock", out var isa))
                return isa;
        throw new EntryPointNotFoundException("_NSConcreteStackBlock nicht gefunden.");
    }

    private static Descriptor* CreateDescriptor()
    {
        var descriptor = (Descriptor*)NativeMemory.AllocZeroed((nuint)sizeof(Descriptor));
        descriptor->Size = (nuint)sizeof(Literal);
        return descriptor; // lives for the process lifetime
    }

    public static long Register(object state)
    {
        long id = Interlocked.Increment(ref nextId);
        Registry[id] = state;
        return id;
    }

    public static void Unregister(long id) => Registry.TryRemove(id, out _);

    /// <summary>Resolves the managed state of a block from inside its invoke function.</summary>
    public static T? State<T>(IntPtr block) where T : class =>
        block != IntPtr.Zero && Registry.TryGetValue(((Literal*)block)->Context, out var state) ? state as T : null;

    /// <summary>Registry id stored in a block literal (original or heap copy).</summary>
    public static long ContextOf(IntPtr block) => block == IntPtr.Zero ? 0 : ((Literal*)block)->Context;

    public static IntPtr Create(IntPtr invoke, long contextId)
    {
        var literal = (Literal*)NativeMemory.AllocZeroed((nuint)sizeof(Literal));
        literal->Isa = StackBlockIsa;
        literal->Invoke = invoke;
        literal->Descriptor = SharedDescriptor;
        literal->Context = contextId;
        return (IntPtr)literal;
    }

    /// <summary>Frees our literal after the Objective-C call returned (the callee holds its own heap copy if needed).</summary>
    public static void Free(IntPtr block) { if (block != IntPtr.Zero) NativeMemory.Free((void*)block); }
}
