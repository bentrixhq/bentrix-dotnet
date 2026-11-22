// The C# MCP client's walk against a running BENTRIX (bentrix --serve over the CI tape): it finds the
// application and pairs with nothing typed, reads its state through the generated surface, reads a
// resource by its template, and runs the same Gym episode every binding runs - through the agents'
// tools, over the wire - printed as JSON for sdk/python/tests/test_crosslang.py.
//
//   LOCALAPPDATA=<the serve's> dotnet run --project sdk/csharp/mcpwalk

using System.Text.Json.Nodes;
using Bentrix.Mcp;

static void Check(bool held, string what)
{
    if (!held)
    {
        throw new BentrixException($"FAILED: {what}");
    }
}

try
{
    using var b = await BentrixClient.ConnectAsync("dotnet-walk");
    var state = await b.Workspace.GetState();
    var bench = state!["laboratories"]![0]!["benches"]![0]!["uri"]!.GetValue<string>();
    var p = bench.Replace("bentrix://", "").Split('/');
    var book = await b.Trade.Book(p[0], p[1], p[3]);
    Check(book?["positions"] is JsonArray, $"the book did not read: {book}");
    var said = "";
    try
    {
        await b.Call("no_such_tool");
    }
    catch (BentrixException e)
    {
        said = e.Message;
    }
    Check(said.Contains("no_such_tool"), $"a refusal did not say its tool: {said}");

    var made = await b.Strategy.EnvMake(bench, interval: "H1", window: 8, episode: "window", episodeBars: 12);
    var env = made!["env"]!.GetValue<string>();
    var start = await b.Strategy.EnvReset(env, seed: 5);
    Check(start!["obs"]!["market"]!.AsArray().Count == 8, "the observation is not shaped as declared");
    var rewards = new List<double>();
    var hashes = new List<string> { start["info"]!["ledger_hash"]!.GetValue<string>() };
    foreach (var a in new[] { 1, 1, 2, 0, 2, 2, 1, 0, 1, 2, 0, 1 })
    {
        var s = await b.Strategy.EnvStep(env, a);
        rewards.Add(s!["reward"]!.GetValue<double>());
        hashes.Add(s["info"]!["ledger_hash"]!.GetValue<string>());
        if (s["terminated"]!.GetValue<bool>() || s["truncated"]!.GetValue<bool>())
        {
            break;
        }
    }
    await b.Strategy.EnvClose(env);
    // A tool's own refusal (isError), not a request the wire refused.
    Exception? refusal = null;
    try
    {
        await b.Strategy.EnvStep(env, 0);
    }
    catch (Exception e)
    {
        refusal = e;
    }
    Check(refusal is ToolException && refusal.Message.Contains("No environment"), $"a closed environment's step was not refused in its words: {refusal}");
    Check(hashes.Distinct().Count() > 1, "the episode traded nothing");
    // System.Text.Json writes a double in the shortest digits that read back as the same double.
    var episode = new JsonObject
    {
        ["start_ms"] = start["info"]!["start_ms"]!.GetValue<long>(),
        ["rewards"] = new JsonArray(rewards.Select(r => (JsonNode)JsonValue.Create(r)).ToArray()),
        ["hashes"] = new JsonArray(hashes.Select(h => (JsonNode)JsonValue.Create(h)).ToArray()),
    };
    Console.WriteLine(episode.ToJsonString());
    Console.WriteLine("PASS");
    return 0;
}
catch (BentrixException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
