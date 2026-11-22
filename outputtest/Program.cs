// Model outputs of every kind and the person's meaning: every kind builds a document; a value outside its
// declared range is refused in words before anything is sent; three examples are three meanings; a put
// never sends an arrival.
//
// Mutation (2026-11-11), red: InRange not comparing - a 1.3 for a 0..1 output was sent.
using System.Text.Json.Nodes;
using Bentrix.Mcp;

var failed = 0;
void Check(bool c, string what)
{
    if (!c)
    {
        Console.WriteLine("FAIL: " + what);
        failed++;
    }
}

bool Refused(Action a, string? needle = null)
{
    try { a(); return false; }
    catch (OutputRefused e) { return needle == null || e.Message.Contains(needle); }
}

(Caller, List<(string Name, IDictionary<string, object?> Args)>) Rec()
{
    var calls = new List<(string, IDictionary<string, object?>)>();
    Caller c = (n, a) => { calls.Add((n, a)); return Task.FromResult<JsonNode?>(new JsonObject { ["ok"] = true }); };
    return (c, calls);
}

var unit = (0.0, 1.0);
var built = new[]
{
    Output.Scalar("m1", "o1", "EURUSD", 1, unit, 0.7),
    Output.Classes("m1", "o1", "EURUSD", 1, new[] { "up", "down" }, true, new Dictionary<string, double> { ["up"] = 0.6, ["down"] = 0.4 }),
    Output.Quantiles("m1", "o1", "EURUSD", 1, new[] { 0.1, 0.9 }, new Dictionary<string, double> { ["0.1"] = 1.0, ["0.9"] = 1.2 }),
    Output.Rank("m1", "o1", "AAPL", 1, 4871, 0.62),
    Output.Position("m1", "o1", "EURUSD", 1, "fraction", -0.5),
    Output.Weights("m1", "o1", "EURUSD", 1, true, new Dictionary<string, double> { ["EURUSD"] = 0.6, ["GBPUSD"] = 0.4 }),
    Output.Event("m1", "o1", "EURUSD", 1, 0.8, 3),
    Output.Levels("m1", "o1", "EURUSD", 1, new Dictionary<string, double> { ["stop"] = 1.09, ["target"] = 1.12 }),
    Output.Action("m1", "o1", "EURUSD", 1, "buy", 0.5),
    Output.Text("m1", "o1", "EURUSD", 1, "the trend held"),
    Output.Series("m1", "o1", "EURUSD", 1, new[] { "a" }, new Dictionary<string, double> { ["a"] = 1 }),
};
var kinds = new HashSet<string> { "path" };
foreach (var o in built)
{
    o.Check();
    Check(o.Spec()["kind"]!.GetValue<string>() == o.Kind, o.Kind + " wrote another kind");
    kinds.Add(o.Kind);
}
Check(Output.Kinds.Length == 12 && kinds.Count == 12, $"the builders cover {kinds.Count} of {Output.Kinds.Length} kinds");

var (r, rc) = Rec();
Check(Refused(() => Output.Scalar("m1", "o1", "EURUSD", 1, unit, 1.3).Put(r, "bench"), "0 to 1; 1.3 is outside it"), "a 1.3 for a 0..1 output was sent");
Check(rc.Count == 0, "something was sent");
Check(Refused(() => Output.Rank("m1", "o1", "AAPL", 1, 10, 1.5).Check()), "a rank of 1.5 was taken");

var (m, mc) = Rec();
foreach (var mm in new[] { OutputMeaning.Of("buy", "sell", 0.6, 0.4), OutputMeaning.Of("buy", "nothing", 0.6), OutputMeaning.Of("sell", "nothing", 0.6) })
    await Output.SetMeaning(m, "bench", "m1", "o1", mm, unit);
Check(mc[2].Name == "output_meaning_set" && ((JsonObject)mc[2].Args["meaning"]!)["high_means"]!.GetValue<string>() == "sell", "the meaning was not sent as set");
Check(Refused(() => Output.SetMeaning(m, "bench", "m1", "o1", OutputMeaning.Of("profit"), unit)), "profit was taken as a meaning's word");
Check(Refused(() => Output.SetMeaning(m, "bench", "m1", "o1", OutputMeaning.Of("buy", null, 1.5), unit)), "a threshold outside the range was taken");

var (p, pc) = Rec();
await Output.Scalar("m1", "dir", "EURUSD", 1, unit, 0.7).Steps(4).Put(p, "bench");
await Output.PutOutputs(p, "bench", new[] { Output.Event("m1", "e", "EURUSD", 1, 0.9) }, true);
var spec = (JsonObject)pc[0].Args["output"]!;
Check(pc[0].Name == "output_put" && spec["horizon"]!["steps"]!.GetValue<int>() == 4, "output_put sent the wrong horizon");
Check(!spec.ContainsKey("put_ms"), "an arrival was sent");
Check(pc[1].Name == "output_put_many" && (bool)pc[1].Args["all_or_nothing"]!, "output_put_many sent the wrong flag");

Console.WriteLine(failed == 0 ? "OutputTest: all passed" : $"OutputTest: {failed} failed");
return failed == 0 ? 0 : 1;
