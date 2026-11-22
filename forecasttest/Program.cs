// A forecast's builder and an evaluation's sheet.
//
// Mutations (2026-11-08), each red: Spec writing the timeframe whatever it is (the element form named an interval);
// declined read as a failure (a decline was EvalFailed).
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

JsonObject Moved(string ask, string state, string? key = null, JsonNode? value = null)
{
    var m = new JsonObject { ["ask"] = ask, ["state"] = state };
    if (key != null) m[key] = value;
    return m;
}

(Caller, List<string>) Sheet(params JsonObject[][] pages)
{
    var queue = new Queue<JsonObject[]>(pages);
    var asked = new List<string>();
    Caller c = (name, args) =>
    {
        if (name != "wait_for" || (string?)args["uri"] != Forecast.EvalEvent) throw new InvalidOperationException("not the eval event");
        var after = (string)args["after"]!;
        asked.Add(after);
        var items = queue.Count > 0 ? queue.Dequeue() : Array.Empty<JsonObject>();
        var arr = new JsonArray();
        foreach (var i in items) arr.Add(i.DeepClone());
        return Task.FromResult<JsonNode?>(new JsonObject { ["items"] = arr, ["next"] = (int.Parse(after) + items.Length).ToString() });
    };
    return (c, asked);
}

var f = Forecast.OfElements("bricks", "EURUSD", "renko", 1700000000000L, new JsonObject { ["renko_box"] = 0.0005 });
f.Step(new ForecastStep(1.1005) { ExpectedMs = 1700000600000L });
var form = f.Spec()["form"]!.AsObject();
Check(!form.ContainsKey("timeframe") && (string?)form["chart_type"] == "renko" && form["elements"] != null, "the element form named an interval");
Check((long?)f.Spec()["steps"]![0]!["expected_ms"] == 1700000600000L, "a step lost its expected_ms");
Check((string?)Forecast.OfTime("m", "EURUSD", "H1", 0).Step(new ForecastStep(1)).Spec()["form"]!["timeframe"] == "H1", "the time form named no interval");

var (none, _) = Sheet();
Check(await Forecast.EvalStarted(none, new JsonObject { ["eval"] = 7 }, 0, 1) == 7, "a run at once");
var ask = new JsonObject { ["ask"] = "bentrix://ask/2", ["watch"] = "bentrix://forecasts/eval?after=0" };
var (s, asked) = Sheet(new[] { Moved("bentrix://ask/1", "declined"), Moved("bentrix://ask/2", "asked") }, new[] { Moved("bentrix://ask/2", "made_laboratory") },
    new[] { Moved("bentrix://ask/2", "started", "eval", 11) });
Check(await Forecast.EvalStarted(s, ask, 0, 3) == 11, "the run the person started");
Check(string.Join(",", asked) == "0,2,3", "the cursor " + string.Join(",", asked));
try
{
    await Forecast.EvalStarted(Sheet(new[] { Moved("bentrix://ask/2", "declined") }).Item1, ask, 0, 3);
    Check(false, "a decline answered a run");
}
catch (DeclinedByThePerson) { }
catch (Exception) { Check(false, "a decline was EvalFailed"); }
try
{
    await Forecast.EvalStarted(Sheet(new[] { Moved("bentrix://ask/2", "failed", "why", "the laboratory was not made") }).Item1, ask, 0, 3);
    Check(false, "a failure answered a run");
}
catch (EvalFailed e) { Check(e.Why == "the laboratory was not made", "a failure lost its why"); }
try
{
    await Forecast.EvalStarted(Sheet().Item1, ask, 0, 2);
    Check(false, "a watch with no answer did not end");
}
catch (TimeoutException) { }
Console.WriteLine(failed == 0 ? "PASS" : "FAILED " + failed);
return failed == 0 ? 0 : 1;
