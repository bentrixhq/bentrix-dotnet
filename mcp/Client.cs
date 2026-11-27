// The hand-written half of Bentrix's C# MCP client: the connection and everything that does not change
// when a capability is added (Generated.cs is the rest, from the catalogue). It speaks MCP revision
// 2026-07-28 over Streamable HTTP with .NET's own HttpClient: every request is its own POST carrying the
// protocol version, the client's identity and its (empty) capabilities in _meta - the same wire as the
// Python client's _runtime.py and the JavaScript client's client.js, field for field.
//
// BentrixClient.ConnectAsync() finds the running application through endpoint.json in Bentrix's agents
// folder, pairs with it through local.secret under a name of this process, and connects with the token
// it gets.

#nullable enable
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bentrix.Mcp;

public class BentrixException : Exception
{
    public BentrixException(string message) : base(message) { }
}

/// <summary>A JSON-RPC error: the request itself was refused.</summary>
public sealed class RpcException : BentrixException
{
    public long Code { get; }
    /// <summary>The wait a busy answer names, in milliseconds; 0 when it names none.</summary>
    public long RetryAfterMs { get; }
    public RpcException(long code, string message, long retryAfterMs = 0) : base($"[{code}] {message}")
    {
        Code = code;
        RetryAfterMs = retryAfterMs;
    }
}

/// <summary>A tool ran and answered isError: the product's own sentence, verbatim.</summary>
public sealed class ToolException : BentrixException
{
    public string Tool { get; }
    public ToolException(string tool, string message) : base($"{tool}: {message}") => Tool = tool;
}

public sealed class NotRunningException : BentrixException
{
    public NotRunningException(string message) : base(message) { }
}

public sealed class PairingRefusedException : BentrixException
{
    public PairingRefusedException(string message) : base(message) { }
}

public sealed class IncompatibleCatalogueException : BentrixException
{
    public IncompatibleCatalogueException(string message) : base(message) { }
}

public sealed class JobFailedException : BentrixException
{
    public JobFailedException(string message) : base(message) { }
}

/// <summary>A long operation: WaitAsync answers its result once it completes (the person may act first).</summary>
public sealed class Job
{
    private readonly Client _c;
    private readonly JsonNode? _result;

    public string? Handle { get; }

    internal Job(Client c, string? handle, JsonNode? result)
    {
        _c = c;
        Handle = handle;
        _result = result;
    }

    public Task<JsonNode?> StatusAsync(CancellationToken ct = default) =>
        Handle is null ? Task.FromResult<JsonNode?>(new JsonObject { ["status"] = "completed" }) : _c.Call("job_get", new Dictionary<string, object?> { ["job"] = Handle }, ct);

    public async Task CancelAsync(CancellationToken ct = default)
    {
        if (Handle is not null)
        {
            await _c.Call("job_cancel", new Dictionary<string, object?> { ["job"] = Handle }, ct);
        }
    }

    public async Task<JsonNode?> WaitAsync(TimeSpan? timeout = null, TimeSpan? poll = null, CancellationToken ct = default)
    {
        if (Handle is null)
        {
            return _result;
        }
        var deadline = timeout is null ? (DateTime?)null : DateTime.UtcNow + timeout.Value;
        for (;;)
        {
            var st = await StatusAsync(ct);
            var status = st?["status"]?.GetValue<string>();
            if (status == "completed")
            {
                return st?["result"] ?? new JsonObject();
            }
            if (status is "failed" or "cancelled")
            {
                throw new JobFailedException($"job {Handle} {status}: {st?["message"]}");
            }
            if (deadline is not null && DateTime.UtcNow >= deadline)
            {
                throw new BentrixException($"job {Handle} is still {status}");
            }
            await Task.Delay(poll ?? TimeSpan.FromMilliseconds(500), ct);
        }
    }
}

/// <summary>One Bentrix endpoint. Subclassed by the generated BentrixClient.</summary>
public class Client : IDisposable, IDoor
{
    public const string ProtocolVersion = "2026-07-28";
    public const string ClientName = "bentrix-dotnet-sdk";
    public const string ClientVersion = "0.1.0";

    /// <summary><b>Busy, try again</b>: the JSON-RPC code Bentrix answers when it refused a request BEFORE
    /// doing anything with it — one program's 20 calls a second spent, too many connections or streams, a
    /// task it could not read in time. Nothing ran, so the same request is sent again after the wait the
    /// answer names (retryAfterMs, a second when none), at most <see cref="BusyRetries"/> times and each
    /// wait at most <see cref="BusyWaitMaxMs"/>; then the refusal is thrown. An order is never sent twice
    /// by this.</summary>
    public const long Busy = -40001;
    public const int BusyRetries = 10;
    public const long BusyWaitMaxMs = 5000;

    /// <summary>A call that asks the server to wait is given that long and this much more (found 2026-11-02:
    /// wait_for beyond the client's own timeout ended in the client before the server could answer).</summary>
    public static readonly TimeSpan LongPollMargin = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly string _token;
    private long _id;

    public string Url { get; }
    public string SdkCatalogueVersion { get; }

    protected Client(string url, string token, string catalogueVersion, TimeSpan? timeout)
    {
        Url = url;
        _token = token;
        SdkCatalogueVersion = catalogueVersion;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Leave out what is null, so the server's own default applies.</summary>
    internal static JsonObject Plain(IDictionary<string, object?>? args)
    {
        var o = new JsonObject();
        foreach (var (k, v) in args ?? new Dictionary<string, object?>())
        {
            if (v is not null)
            {
                o[k] = v is JsonNode n ? n.DeepClone() : JsonSerializer.SerializeToNode(v);
            }
        }
        return o;
    }

    /// <summary>A header value, or the standard Base64 sentinel when plain ASCII cannot carry it.</summary>
    internal static string HeaderValue(string v)
    {
        var safe = v.All(ch => ch >= 0x20 && ch <= 0x7e) && v == v.Trim() && !(v.StartsWith("=?base64?") && v.EndsWith("?="));
        return safe ? v : $"=?base64?{Convert.ToBase64String(Encoding.UTF8.GetBytes(v))}?=";
    }

    private static string Scalar(object v) => v switch
    {
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    /// <summary>The RFC 6570 subset the catalogue uses: {name} and {?a,b}. A null optional is left out.</summary>
    internal static string ExpandTemplate(string template, IDictionary<string, object?>? vars)
    {
        var values = (vars ?? new Dictionary<string, object?>()).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);
        FromAddress(values);
        return Regex.Replace(template, @"\{([?&]?)([^}]+)\}", m =>
        {
            var op = m.Groups[1].Value;
            var names = m.Groups[2].Value;
            if (op.Length > 0)
            {
                var pairs = names.Split(',').Where(values.ContainsKey).Select(n => $"{n}={Uri.EscapeDataString(Scalar(values[n]))}").ToList();
                return pairs.Count > 0 ? op + string.Join("&", pairs) : "";
            }
            if (!values.TryGetValue(names, out var one))
            {
                throw new ArgumentException($"the URI template {template} needs {names}");
            }
            return Uri.EscapeDataString(Scalar(one));
        });
    }

    /// <summary>
    /// A workbench's address stands for its three parts: <c>bench</c> given as a
    /// <c>bentrix://&lt;home&gt;/&lt;owner&gt;/bench/&lt;name&gt;</c> address fills <c>home</c>, <c>owner</c> and
    /// <c>bench</c>. Parts given as before are left as they are.
    /// </summary>
    internal static void FromAddress(IDictionary<string, object> values)
    {
        if (!values.TryGetValue("bench", out var v) || v is not string s || !s.StartsWith("bentrix://", StringComparison.Ordinal))
        {
            return;
        }
        var parts = s.Substring("bentrix://".Length).Split('/');
        if (parts.Length < 4 || parts[2] != "bench")
        {
            return;
        }
        values["home"] = parts[0];
        values["owner"] = parts[1];
        values["bench"] = parts[3];
    }

    /// <summary>One request, sent again while Bentrix answers busy (<see cref="Busy"/>): it ran nothing.</summary>
    private async Task<JsonNode?> Rpc(string method, JsonObject parameters, string? name, CancellationToken ct, TimeSpan? timeout = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // A node has one parent: each attempt sends its own copy of the parameters.
                return await RpcOnce(method, parameters.DeepClone().AsObject(), name, ct, timeout);
            }
            catch (RpcException e) when (e.Code == Busy && attempt < BusyRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(e.RetryAfterMs > 0 ? e.RetryAfterMs : 1000, BusyWaitMaxMs)), ct);
            }
        }
    }

    private async Task<JsonNode?> RpcOnce(string method, JsonObject parameters, string? name, CancellationToken ct, TimeSpan? timeout)
    {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timed.CancelAfter(timeout ?? _timeout);
        ct = timed.Token;
        var id = Interlocked.Increment(ref _id);
        parameters["_meta"] = new JsonObject
        {
            ["io.modelcontextprotocol/protocolVersion"] = ProtocolVersion,
            ["io.modelcontextprotocol/clientInfo"] = new JsonObject { ["name"] = ClientName, ["version"] = ClientVersion },
            ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject(),
        };
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
        using var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.Add("MCP-Protocol-Version", ProtocolVersion);
        req.Headers.Add("Mcp-Method", method);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (name is not null)
        {
            req.Headers.Add("Mcp-Name", HeaderValue(name));
        }
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonNode? message;
        if (resp.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            message = ReadSse(text, id);
        }
        else
        {
            try
            {
                message = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                throw new RpcException((long)resp.StatusCode, $"HTTP {(int)resp.StatusCode} with no JSON-RPC body");
            }
        }
        if (message?["error"] is JsonNode err)
        {
            long after = err["data"] is JsonObject data && data["retryAfterMs"] is JsonValue v && v.TryGetValue<long>(out var ms) ? ms : 0;
            throw new RpcException(err["code"]?.GetValue<long>() ?? (long)resp.StatusCode, err["message"]?.GetValue<string>() ?? "", after);
        }
        return message?["result"];
    }

    /// <summary>The final response on a request-scoped event stream; notifications before it are ignored.</summary>
    private static JsonNode ReadSse(string text, long id)
    {
        var data = new List<string>();
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')).Append(""))
        {
            if (line.Length == 0)
            {
                if (data.Count > 0)
                {
                    try
                    {
                        var msg = JsonNode.Parse(string.Join("\n", data));
                        if (msg?["id"]?.GetValue<long>() == id && (msg["result"] is not null || msg["error"] is not null))
                        {
                            return msg;
                        }
                    }
                    catch (Exception)
                    {
                        // not ours
                    }
                }
                data.Clear();
            }
            else if (line.StartsWith("data:"))
            {
                data.Add(line[5..].TrimStart());
            }
        }
        throw new BentrixException("the stream ended without a response");
    }

    /// <summary>A tool by its wire name; null arguments are left out.</summary>
    public async Task<JsonNode?> Call(string name, IDictionary<string, object?>? args = null, CancellationToken ct = default)
    {
        var plain = Plain(args);
        var wait = plain["timeout_ms"] is JsonValue w && w.TryGetValue<double>(out var ms) ? TimeSpan.FromMilliseconds(ms) + LongPollMargin : TimeSpan.Zero;
        var res = await Rpc("tools/call", new JsonObject { ["name"] = name, ["arguments"] = plain }, name, ct, wait > _timeout ? wait : _timeout);
        if (res?["resultType"]?.GetValue<string>() == "input_required")
        {
            throw new BentrixException($"{name} needs input this client cannot give; answer it in Bentrix's window");
        }
        var text = string.Concat((res?["content"]?.AsArray() ?? new JsonArray()).Where(c => c?["type"]?.GetValue<string>() == "text").Select(c => c!["text"]!.GetValue<string>()));
        if (res?["isError"]?.GetValue<bool>() == true)
        {
            throw new ToolException(name, text.Length > 0 ? text : "the tool failed");
        }
        if (res?["structuredContent"] is JsonNode structured)
        {
            return structured.DeepClone();
        }
        try
        {
            return text.Length > 0 ? JsonNode.Parse(text) : new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    /// <summary>A long operation: a Job whether the server answered at once or with a handle.</summary>
    public async Task<Job> CallJob(string name, IDictionary<string, object?>? args = null, CancellationToken ct = default)
    {
        var raw = await Call(name, args, ct);
        if (raw is JsonObject o && o.Count == 1 && o["job"] is JsonValue v && v.TryGetValue<string>(out var handle))
        {
            return new Job(this, handle, null);
        }
        return new Job(this, null, raw);
    }

    private async Task<JsonNode?> ReadUri(string uri, CancellationToken ct)
    {
        var res = await Rpc("resources/read", new JsonObject { ["uri"] = uri }, uri, ct);
        var first = res?["contents"]?.AsArray().FirstOrDefault();
        if (first?["text"] is not JsonValue t || !t.TryGetValue<string>(out var text))
        {
            throw new BentrixException($"{uri} answered no text content");
        }
        return JsonNode.Parse(text);
    }

    public Task<JsonNode?> ReadResource(string template, IDictionary<string, object?>? vars = null, CancellationToken ct = default) =>
        ReadUri(ExpandTemplate(template, vars), ct);

    /// <summary>Every page of a cursor-paged resource: next is the URI of the following page.</summary>
    public async IAsyncEnumerable<JsonNode?> ReadPages(string template, IDictionary<string, object?>? vars = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? uri = ExpandTemplate(template, vars);
        while (!string.IsNullOrEmpty(uri))
        {
            var page = await ReadUri(uri, ct);
            yield return page;
            uri = page?["next"]?.GetValue<string>();
        }
    }

    /// <summary>Items of a stream, forever: wait_for long-polls, a page is { items, next }.</summary>
    public async IAsyncEnumerable<JsonNode?> Watch(string template, IDictionary<string, object?>? vars = null, string? after = null, int timeoutMs = 30000, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var uri = ExpandTemplate(template, vars);
        var cursor = after;
        for (;;)
        {
            var page = await Call("wait_for", new Dictionary<string, object?> { ["uri"] = uri, ["timeout_ms"] = timeoutMs, ["after"] = cursor }, ct);
            foreach (var item in page?["items"]?.AsArray() ?? new JsonArray())
            {
                yield return item?.DeepClone();
            }
            if (page?["next"] is JsonValue next && next.TryGetValue<string>(out var n))
            {
                cursor = n;
            }
        }
    }

    /// <summary>Refuse a server on another major line of the catalogue.</summary>
    public async Task<string> VerifyCatalogue(CancellationToken ct = default)
    {
        var me = await Call("agent_whoami", null, ct);
        var server = me?["catalogue_version"]?.GetValue<string>() ?? "";
        if (Major(server) != Major(SdkCatalogueVersion))
        {
            throw new IncompatibleCatalogueException($"this SDK was generated from catalogue {SdkCatalogueVersion}; the server's is {server}");
        }
        return server;
    }

    private static int Major(string v)
    {
        var p = v.Split('.');
        if (p.Length != 3 || !p.All(x => x.Length > 0 && x.All(char.IsAsciiDigit)))
        {
            throw new IncompatibleCatalogueException($"{v} is not a version major.minor.patch");
        }
        return int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Finding the running application and pairing with it.</summary>
public static class Discovery
{
    public const string EndpointFile = "endpoint.json";
    public const string SecretFile = "local.secret";

    /// <summary>The folder Bentrix writes its endpoint and secret into - user_dir(Data, "agents"), as the application has it.</summary>
    public static string AgentsDir()
    {
        var lad = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(lad))
        {
            return Path.Combine(lad, "BENTRIX", "agents");
        }
        var home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrEmpty(home) ? "bentrix-agents" : Path.Combine(home, ".local", "share", "bentrix", "agents");
    }

    public static string ReadEndpoint(string folder)
    {
        var file = Path.Combine(folder, EndpointFile);
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(File.ReadAllText(file));
        }
        catch (Exception)
        {
            throw new NotRunningException($"Bentrix is not running: there is no readable {EndpointFile} in {folder}");
        }
        var url = doc?["url"] is JsonValue u && u.TryGetValue<string>(out var s) ? s : null;
        var pid = doc?["pid"] is JsonValue p && p.TryGetValue<long>(out var n) ? n : (long?)null;
        if (url is null || !url.StartsWith("http://127.0.0.1:") || pid is null)
        {
            throw new NotRunningException($"Bentrix is not running: {file} is not an endpoint file");
        }
        try
        {
            using var _ = Process.GetProcessById((int)pid.Value);
        }
        catch (ArgumentException)
        {
            throw new NotRunningException($"Bentrix is not running: {file} is stale (process {pid} has ended)");
        }
        return url;
    }

    public static string ReadSecret(string folder)
    {
        string secret;
        try
        {
            secret = File.ReadAllText(Path.Combine(folder, SecretFile)).Trim();
        }
        catch (Exception)
        {
            throw new PairingRefusedException($"there is no readable {SecretFile} in {folder}: Bentrix writes it at every start");
        }
        if (secret.Length == 0)
        {
            throw new PairingRefusedException($"{SecretFile} is empty");
        }
        return secret;
    }

    /// <summary>The name this process pairs under: BENTRIX_AGENT_NAME, else the program's name.</summary>
    public static string DefaultName()
    {
        var env = Environment.GetEnvironmentVariable("BENTRIX_AGENT_NAME")?.Trim();
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }
        var program = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs().FirstOrDefault() ?? "");
        return program.Length > 0 ? $"{Client.ClientName} ({program})" : Client.ClientName;
    }

    /// <summary>Present the secret to url/pair and answer the token. The secret travels in this one body.</summary>
    public static async Task<string> PairAsync(string url, string secret, string name, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        var body = new JsonObject { ["secret"] = secret, ["client"] = new JsonObject { ["name"] = name } };
        using var resp = await http.PostAsync(url.TrimEnd('/') + "/pair", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        JsonNode? doc = null;
        try
        {
            doc = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (JsonException)
        {
            // no body
        }
        if (!resp.IsSuccessStatusCode)
        {
            var code = doc?["error"]?["code"]?.GetValue<string>() ?? "";
            if (code == "wrong_secret")
            {
                throw new PairingRefusedException("Bentrix refused the secret in the secret file: it writes a new secret at every start");
            }
            throw new PairingRefusedException($"Bentrix answered the pairing request with HTTP {(int)resp.StatusCode} {code}: {doc?["error"]?["message"]}");
        }
        var token = doc?["token"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : null;
        if (string.IsNullOrEmpty(token))
        {
            throw new PairingRefusedException("Bentrix answered the pairing request with no token");
        }
        return token;
    }

    /// <summary>Find the running application and get a token: (url, token).</summary>
    public static async Task<(string Url, string Token)> DiscoverAsync(string? agentsFolder = null, string? name = null, CancellationToken ct = default)
    {
        var folder = agentsFolder ?? AgentsDir();
        var url = ReadEndpoint(folder);
        return (url, await PairAsync(url, ReadSecret(folder), name ?? DefaultName(), ct));
    }
}
