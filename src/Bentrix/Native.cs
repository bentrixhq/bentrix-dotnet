// **The C ABI, as .NET calls it** (crates/bentrix-ffi/include/bentrix.h): thirteen functions, UTF-8
// text in and out. A char * answered is ours to release with bx_free; NULL or -1 is a failure whose
// words bx_last_error holds on this thread.

using System.Runtime.InteropServices;

namespace Bentrix;

/// <summary>A refusal or a failure, in Bentrix's own words.</summary>
public sealed class BentrixException : Exception
{
    public BentrixException(string message) : base(message) { }
}

internal static class Native
{
    // `libbentrix` on every platform: .NET adds the suffix (.dll, .so, .dylib).
    private const string Lib = "libbentrix";

    // **Where the library is** (the installer puts it in Bentrix's own folder): the file BENTRIX_FFI names, else
    // `libbentrix` in the folder BENTRIX_FFI_DIR names, else on Windows %LOCALAPPDATA%\Programs\BENTRIX, else
    // .NET's own search.
    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, assembly, path) =>
        {
            if (name != Lib) return IntPtr.Zero;
            string file = OperatingSystem.IsWindows() ? "libbentrix.dll" : OperatingSystem.IsMacOS() ? "libbentrix.dylib" : "libbentrix.so";
            var places = new List<string>();
            var named = Environment.GetEnvironmentVariable("BENTRIX_FFI");
            if (!string.IsNullOrEmpty(named)) places.Add(named);
            var dir = Environment.GetEnvironmentVariable("BENTRIX_FFI_DIR");
            if (!string.IsNullOrEmpty(dir)) places.Add(Path.Combine(dir, file));
            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(local)) places.Add(Path.Combine(local, "Programs", "BENTRIX", file));
            foreach (var p in places)
            {
                if (File.Exists(p) && NativeLibrary.TryLoad(p, out var handle)) return handle;
            }
            return IntPtr.Zero;
        });
    }

    /// <summary>**The engine's interface this package speaks** (<c>bx_abi</c>): a library of another one is refused before <c>bx_open</c>, in a sentence naming both.</summary>
    internal const uint Abi = 2;

    [DllImport(Lib)] private static extern uint bx_abi();

    /// <summary>The interface the library speaks: its <c>bx_abi()</c>, or 1 for a library from before it (0.1.382).</summary>
    internal static uint AbiOfTheLibrary()
    {
        try { return bx_abi(); }
        catch (EntryPointNotFoundException) { return 1; }
    }

    /// <summary>Refused, in words, unless the library speaks <see cref="Abi"/>.</summary>
    internal static void RefuseAnotherInterface()
    {
        var have = AbiOfTheLibrary();
        if (have == Abi) return;
        var built = Marshal.PtrToStringUTF8(bx_version()) ?? "?";
        var update = have < Abi ? "update Bentrix" : "update this package";
        throw new BentrixException($"Bentrix's engine library speaks interface {have} (Bentrix {built}) and this package speaks {Abi}: {update}.");
    }

    [DllImport(Lib)] internal static extern IntPtr bx_version();
    [DllImport(Lib)] internal static extern IntPtr bx_last_error();
    [DllImport(Lib)] internal static extern void bx_free(IntPtr p);
    [DllImport(Lib)] internal static extern IntPtr bx_open([MarshalAs(UnmanagedType.LPUTF8Str)] string? workspace, [MarshalAs(UnmanagedType.LPUTF8Str)] string? store, [MarshalAs(UnmanagedType.LPUTF8Str)] string? client);
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
