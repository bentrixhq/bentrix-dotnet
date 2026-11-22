// **The C ABI, as .NET calls it** (crates/bentrix-ffi/include/bentrix.h): thirteen functions, UTF-8
// text in and out. A char * answered is ours to release with bx_free; NULL or -1 is a failure whose
// words bx_last_error holds on this thread.

using System.Runtime.InteropServices;

namespace Bentrix;

/// <summary>A refusal or a failure, in BENTRIX's own words.</summary>
public sealed class BentrixException : Exception
{
    public BentrixException(string message) : base(message) { }
}

internal static class Native
{
    // `libbentrix` on every platform: .NET adds the suffix (.dll, .so, .dylib).
    private const string Lib = "libbentrix";

    [DllImport(Lib)] internal static extern IntPtr bx_version();
    [DllImport(Lib)] internal static extern IntPtr bx_last_error();
    [DllImport(Lib)] internal static extern void bx_free(IntPtr p);
    [DllImport(Lib)] internal static extern IntPtr bx_open([MarshalAs(UnmanagedType.LPUTF8Str)] string? workspace, [MarshalAs(UnmanagedType.LPUTF8Str)] string? store);
    [DllImport(Lib)] internal static extern void bx_close(IntPtr s);
    [DllImport(Lib)] internal static extern IntPtr bx_call(IntPtr s, [MarshalAs(UnmanagedType.LPUTF8Str)] string tool, [MarshalAs(UnmanagedType.LPUTF8Str)] string? argsJson);
    [DllImport(Lib)] internal static extern IntPtr bx_read(IntPtr s, [MarshalAs(UnmanagedType.LPUTF8Str)] string resource, [MarshalAs(UnmanagedType.LPUTF8Str)] string? varsJson);
    [DllImport(Lib)] internal static extern int bx_arm(IntPtr s, [MarshalAs(UnmanagedType.LPUTF8Str)] string bench, UIntPtr maxFrames);
    [DllImport(Lib)] internal static extern int bx_advance(IntPtr s, [MarshalAs(UnmanagedType.LPUTF8Str)] string bench, long toMs, out long reached);
    [DllImport(Lib)] internal static extern int bx_frame(IntPtr s);
    [DllImport(Lib)] internal static extern int bx_settle(IntPtr s, UIntPtr maxFrames);
    [DllImport(Lib)] internal static extern long bx_interval_ms([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Lib)] internal static extern int bx_make_tape([MarshalAs(UnmanagedType.LPUTF8Str)] string directory, out IntPtr workspace, out IntPtr store);

    internal static string LastError() => Marshal.PtrToStringUTF8(bx_last_error()) ?? "";

    /// <summary>An answered char * as text, released; a NULL as the refusal it was.</summary>
    internal static string Take(IntPtr p)
    {
        if (p == IntPtr.Zero)
        {
            throw new BentrixException(LastError());
        }
        try
        {
            return Marshal.PtrToStringUTF8(p) ?? "";
        }
        finally
        {
            bx_free(p);
        }
    }

    internal static void Done(int status)
    {
        if (status != 0)
        {
            throw new BentrixException(LastError());
        }
    }
}
