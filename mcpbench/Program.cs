// The C# Bench's walk against a running Bentrix (bentrix --serve over a practice tape): the common tasks - a refusal
// read as an answer, a step, a buy with a stop and a target, a modify, a part closed, CloseAll, the account and the
// deals - through Bench.cs, the layer generated from the Bench table. What it saw is printed as JSON for
// sdk/python/tests/test_bench.py, which holds it to the engine's own walk over the same tape.
//
//   LOCALAPPDATA=<the serve's> dotnet run --project sdk/csharp/mcpbench              (a running Bentrix)
//   BENTRIX_PRACTICE=<a practice folder> dotnet run --project sdk/csharp/mcpbench    (the engine in this process)

using System.Text.Json.Nodes;
using Bentrix.Mcp;

static void Check(bool held, string what)
{
    if (!held)
    {
        throw new BentrixException($"FAILED: {what}");
    }
}

static double D(JsonNode? n) => n!.GetValue<double>();

try
{
    // One walk, either door: the engine in this process over a practice folder, or the running Bentrix.
    var practice = Environment.GetEnvironmentVariable("BENTRIX_PRACTICE");
    using var local = string.IsNullOrEmpty(practice) ? null : new global::Bentrix.Local(Path.Combine(practice, "ws"), Path.Combine(practice, "store"));
    using var b = local is null ? await BentrixClient.ConnectAsync("dotnet-bench") : null;
    var bench = await Bench.Open(local is null ? b! : new LocalDoor(local));
    var markets = await bench.Markets();
    Check(markets.Count == 1 && markets[0] == "XAUUSD", $"the markets: {string.Join(",", markets)}");
    var refused = await bench.Buy(0.001);
    Check(!refused.Ok && refused.Kind == "volume_out_of_range", $"the refusal: {refused.Raw}");
    Check(refused.Reason is not null && refused.Remedy!.Contains("volume_min") && refused.Docs!.EndsWith("go/concept.volume/"), $"the refusal's words: {refused.Raw}");
    var first = await bench.Step(interval: "H1");
    Check(first is not null && D(first["time"]) > 0 && D(first["close"]) > 0, $"the first step: {first}");
    var group = await bench.Group("walk");
    Check(group is not null && group.ToString() == "1", $"a group's number: {group}");
    var closed = await bench.Bars(interval: "H1", limit: 5);
    Check(closed.Count == 1 && D(closed[0]![0]) == D(first!["time"]), $"the closed candles: {closed.ToJsonString()}");
    var q = await bench.Quote();
    Check(q is not null && D(q["ask"]) > D(q["bid"]), $"the quote: {q}");
    var sl = Math.Round(D(q!["ask"]) - 30.0, MidpointRounding.ToEven);
    var tp = Math.Round(D(q["ask"]) + 30.0, MidpointRounding.ToEven);
    Check((await bench.Buy(0.1, sl: sl, tp: tp)).Ok, "the buy");
    await bench.Step(interval: "H1");
    var positions = await bench.Positions();
    Check(positions.Count == 1, $"the positions: {positions}");
    var position = positions[0]!;
    Check(position["side"]!.GetValue<string>() == "buy" && D(position["volume"]) == 0.1 && D(position["sl"]) == sl && D(position["tp"]) == tp, $"the position: {position}");
    Check((await bench.Modify(position, sl: sl + 5.0)).Ok, "the modify");
    var moved = (await bench.Positions())[0]!;
    Check(D(moved["sl"]) == sl + 5.0 && D(moved["tp"]) == tp, $"only what was named changes: {moved}");
    Check((await bench.Close(moved, lots: 0.04)).Ok, "the part closed");
    await bench.Step(interval: "H1");
    var rest = (await bench.Positions())[0]!;
    Check(D(rest["volume"]) == 0.06, $"the rest: {rest}");
    Check((await bench.CloseAll()).Ok, "CloseAll");
    await bench.Step(interval: "H1");
    Check((await bench.Positions()).Count == 0, "a position stayed");
    var nothing = await bench.CloseAll();
    Check(nothing.Ok && nothing.Raw?["closed"]?.GetValue<long>() == 0, $"CloseAll with nothing open: {nothing.Raw}");
    var account = await bench.Account();
    Check(D(account!["equity"]) == D(account["balance"]) && D(account["balance"]) != 10000.0, $"the account: {account}");
    var deals = await bench.Deals();
    var entries = deals.Select(d => d!["entry"]!.GetValue<string>()).ToList();
    Check(entries.Count(e => e == "in") == 1 && entries.Count(e => e == "out") == 2, $"the deals: {string.Join(",", entries)}");
    var kept = await bench.Keep("kept");
    Check((await kept.Positions()).Count == 0, "the kept workbench reads");
    // Past the days' end, advance stands at their last moment, by either door.
    var end = await bench.Advance(4102444800000L);
    Check(end is > 0 and < 4102444800000L, $"the days' end: {end}");
    if (b is not null)
    {
        // A workbench resource by its address: the same book as by its three parts.
        var p = bench.Uri.Substring("bentrix://".Length).Split('/');
        var byParts = (await b.Trade.Book(p[0], p[1], p[3]))!.ToJsonString();
        Check((await b.Trade.Book(bench))!.ToJsonString() == byParts, "the book by the workbench");
    }
    var seen = new JsonObject
    {
        ["refused"] = new JsonArray(refused.Outcome, refused.Kind, refused.Reason),
        ["first"] = first!.DeepClone(),
        ["group"] = JsonNode.Parse(group!.ToString()!),
        ["bars"] = new JsonArray(closed[0]![0]!.DeepClone()),
        ["deals"] = new JsonArray(deals.Select(d => (JsonNode)new JsonArray(d!["entry"]!.DeepClone(), d["side"]!.DeepClone(), d["volume"]!.DeepClone())).ToArray()),
        ["balance"] = account["balance"]!.DeepClone(),
        ["end"] = end,
    };
    Console.WriteLine(seen.ToJsonString());
    Console.WriteLine("PASS");
    return 0;
}
catch (BentrixException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
