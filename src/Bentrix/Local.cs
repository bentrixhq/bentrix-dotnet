// **BENTRIX in your own .NET process**: an embedded session over a workspace and its recordings, the
// agents' own tools answered in-process by the same names and in the same shapes as over MCP - read,
// laboratory and paper, never a real account (whose orders the person confirms in the window).

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bentrix;

public sealed class Local : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private IntPtr _session;

    /// <summary>Open <paramref name="workspace"/> (yours when null) and its recordings (yours when null).
    /// A window or a --serve already on the workspace refuses it by name.</summary>
    public Local(string? workspace = null, string? store = null)
    {
        _session = Native.bx_open(workspace, store);
        if (_session == IntPtr.Zero)
        {
            throw new BentrixException(Native.LastError());
        }
    }

    /// <summary>The build of the engine this binding loaded.</summary>
    public static string Version => System.Runtime.InteropServices.Marshal.PtrToStringUTF8(Native.bx_version()) ?? "";

    /// <summary>An interval's length in ms (M1, H4, D1); 0 for one with no fixed length.</summary>
    public static long IntervalMs(string label) => Native.bx_interval_ms(label);

    /// <summary>The CI tape in <paramref name="directory"/>: a small, deterministic, licence-clear recording
    /// and a workspace over it.</summary>
    public static (string Workspace, string Store) MakeTape(string directory)
    {
        Native.Done(Native.bx_make_tape(directory, out var ws, out var store));
        return (Native.Take(ws), Native.Take(store));
    }

    private IntPtr Session => _session != IntPtr.Zero ? _session : throw new ObjectDisposedException(nameof(Local));

    /// <summary>One tool by name; its answer, or its refusal raised in its own words. <paramref name="args"/>
    /// is any object JSON can carry (an anonymous object, a dictionary); a null field is left out.</summary>
    public JsonNode? Call(string tool, object? args = null)
    {
        var json = args is null ? null : JsonSerializer.Serialize(args, Json);
        return JsonNode.Parse(Native.Take(Native.bx_call(Session, tool, json)));
    }

    /// <summary>One resource by its catalogue name (trade_book, trade_deals), its template's variables.</summary>
    public JsonNode? Read(string resource, IReadOnlyDictionary<string, string>? vars = null)
    {
        var json = vars is null ? null : JsonSerializer.Serialize(vars, Json);
        return JsonNode.Parse(Native.Take(Native.bx_read(Session, resource, json)));
    }

    /// <summary>Arm a workbench's engine (its venue over its recording), waiting up to maxFrames.</summary>
    public void Arm(string bench, int maxFrames = 20_000) => Native.Done(Native.bx_arm(Session, bench, (UIntPtr)maxFrames));

    /// <summary>Advance a workbench to <paramref name="toMs"/> on its clock, as Play would; where it stands.</summary>
    public long Advance(string bench, long toMs)
    {
        Native.Done(Native.bx_advance(Session, bench, toMs, out var reached));
        return reached;
    }

    /// <summary>Frames until nothing waits, or maxFrames.</summary>
    public void Settle(int maxFrames = 1_000) => Native.Done(Native.bx_settle(Session, (UIntPtr)maxFrames));

    /// <summary>A workbench's open positions and pending orders.</summary>
    public JsonNode? Book(string bench)
    {
        var p = bench.Replace("bentrix://", "").Split('/');
        return Read("trade_book", new Dictionary<string, string> { ["home"] = p[0], ["owner"] = p[1], ["bench"] = p[3] });
    }

    public void Dispose()
    {
        if (_session != IntPtr.Zero)
        {
            Native.bx_close(_session);
            _session = IntPtr.Zero;
        }
    }
}
