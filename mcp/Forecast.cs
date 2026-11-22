// A model's forecast on the chart, built by hand: the forecast_spec document step by step - a time form
// (an interval) or an element form (a chart built by price: renko, kagi, line_break, point_figure, range_bars - no
// interval, its rule in form.elements) - put through forecast_put; and an evaluation's sheet waited on:
// forecast_eval_make's answer is the run at once, or an ask the person answers in Bentrix's window, watched on the
// bentrix://forecasts/eval event. A forecast never names its arrival: Bentrix stamps put_ms.
using System.Text.Json.Nodes;

namespace Bentrix.Mcp;

/// <summary>What a forecast is put through: <c>(n, a) =&gt; client.Call(n, a)</c>, or a test's script.</summary>
public delegate Task<JsonNode?> Caller(string name, IDictionary<string, object?> args);

/// <summary>One bar of a forecast; everything but Close left out when null or empty.</summary>
public sealed class ForecastStep
{
    public double Close;
    public double? Open, High, Low, Volume;
    /// <summary>When the step is expected - on every step or none; what places an element form on a chart by time.</summary>
    public long? ExpectedMs;
    public List<(double Lo, double Hi, double? P)> Bands = new();
    public Dictionary<string, double> Quantiles = new();
    public List<double> Samples = new();

    public ForecastStep(double close) => Close = close;

    internal JsonObject Value()
    {
        var s = new JsonObject { ["close"] = Close };
        if (Open is { } o) s["open"] = o;
        if (High is { } h) s["high"] = h;
        if (Low is { } l) s["low"] = l;
        if (Volume is { } v) s["volume"] = v;
        if (ExpectedMs is { } t) s["expected_ms"] = t;
        if (Bands.Count > 0)
        {
            var a = new JsonArray();
            foreach (var (lo, hi, p) in Bands)
            {
                var b = new JsonObject { ["lo"] = lo, ["hi"] = hi };
                if (p is { } pp) b["p"] = pp;
                a.Add(b);
            }
            s["bands"] = a;
        }
        if (Quantiles.Count > 0)
        {
            var q = new JsonObject();
            foreach (var (k, x) in Quantiles) q[k] = x;
            s["quantiles"] = q;
        }
        if (Samples.Count > 0)
        {
            var a = new JsonArray();
            foreach (var x in Samples) a.Add(x);
            s["samples"] = a;
        }
        return s;
    }
}

/// <summary>The person's No on the evaluation's sheet. Not a failure: nothing was made or fetched.</summary>
public sealed class DeclinedByThePerson : Exception
{
    public string Ask { get; }
    public DeclinedByThePerson(string ask) : base("the person declined " + ask) => Ask = ask;
}

/// <summary>A laboratory not made over the span, or a run that did not start on it, with Bentrix's why.</summary>
public sealed class EvalFailed : Exception
{
    public string Ask { get; }
    public string Why { get; }
    public EvalFailed(string ask, string why) : base(why) => (Ask, Why) = (ask, why);
}

/// <summary>The forecast_spec document. AnchorMs is the open of the last closed bar (or element) it is made at.</summary>
public sealed class Forecast
{
    public const string EvalEvent = "bentrix://forecasts/eval";

    public string Model, Symbol, ChartType;
    /// <summary>A time form's interval; null for an element form.</summary>
    public string? Timeframe;
    /// <summary>An element form's rule, where it is not the chart's own.</summary>
    public JsonObject? Elements;
    public long AnchorMs;
    public int Epoch;
    public string? Note;
    public JsonObject? Look;
    public List<ForecastStep> Steps = new();

    private Forecast(string model, string symbol, string chartType, string? timeframe, JsonObject? elements, long anchorMs)
        => (Model, Symbol, ChartType, Timeframe, Elements, AnchorMs) = (model, symbol, chartType, timeframe, elements, anchorMs);

    /// <summary>A forecast of timeframe's bars, drawn as candles.</summary>
    public static Forecast OfTime(string model, string symbol, string timeframe, long anchorMs) => new(model, symbol, "candlestick", timeframe, null, anchorMs);

    /// <summary>A forecast of a chart built by price: no interval, elements its rule (null for the chart's own).</summary>
    public static Forecast OfElements(string model, string symbol, string chartType, long anchorMs, JsonObject? elements = null) => new(model, symbol, chartType, null, elements, anchorMs);

    public Forecast Step(ForecastStep s)
    {
        Steps.Add(s);
        return this;
    }

    /// <summary>The document.</summary>
    public JsonObject Spec()
    {
        var form = new JsonObject { ["chart_type"] = ChartType };
        if (Timeframe != null) form["timeframe"] = Timeframe;
        if (Elements != null) form["elements"] = Elements.DeepClone();
        var steps = new JsonArray();
        foreach (var s in Steps) steps.Add(s.Value());
        var d = new JsonObject
        {
            ["schema_version"] = 1, ["model"] = Model, ["symbol"] = Symbol, ["form"] = form,
            ["anchor"] = new JsonObject { ["time_ms"] = AnchorMs }, ["steps"] = steps, ["epoch"] = Epoch,
        };
        if (Note != null) d["note"] = Note;
        if (Look != null) d["look"] = Look.DeepClone();
        return d;
    }

    /// <summary>forecast_put on the workbench bench: its arrival, its state, where it stands on each chart.</summary>
    public Task<JsonNode?> Put(Caller c, string bench) => c("forecast_put", new Dictionary<string, object?> { ["bench"] = bench, ["forecast"] = Spec() });

    /// <summary>forecast_put_many: several markets and models at one instant.</summary>
    public static Task<JsonNode?> PutMany(Caller c, string bench, IEnumerable<Forecast> fs, bool allOrNothing = false)
    {
        var specs = new JsonArray();
        foreach (var f in fs) specs.Add(f.Spec());
        return c("forecast_put_many", new Dictionary<string, object?> { ["bench"] = bench, ["forecasts"] = specs, ["all_or_nothing"] = allOrNothing });
    }

    /// <summary>A number of the answer as a whole number, however the node holds it.</summary>
    private static long Whole(JsonValue v) =>
        v.TryGetValue<long>(out var l) ? l : v.TryGetValue<int>(out var i) ? i : (long)double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The run to drive, from forecast_eval_make's answer: its eval at once, or - over days the workbench's
    /// laboratory does not hold - the person's answer on the sheet, watched on EvalEvent: started answers the run's id;
    /// declined throws DeclinedByThePerson; failed throws EvalFailed. No tool answers the sheet. idleLimit waits in a
    /// row with no move of this ask end the watch (never, when 0).</summary>
    public static async Task<long> EvalStarted(Caller c, JsonNode made, long timeoutMs = 60000, int idleLimit = 0)
    {
        if (made["eval"] is JsonValue e) return Whole(e);
        var ask = made["ask"]?.GetValue<string>() ?? "";
        var uri = made["watch"]?.GetValue<string>() ?? EvalEvent;
        var cursor = "0";
        var at = uri.IndexOf("after=", StringComparison.Ordinal);
        if (at >= 0) cursor = uri[(at + 6)..].Split('&')[0];
        uri = uri.Split('{', '?')[0];
        for (var idle = 0; idleLimit == 0 || idle < idleLimit;)
        {
            var page = await c("wait_for", new Dictionary<string, object?> { ["uri"] = uri, ["after"] = cursor, ["timeout_ms"] = timeoutMs });
            if (page?["next"] is JsonValue n) cursor = n.GetValue<string>();
            var moved = false;
            if (page?["items"] is JsonArray items)
            {
                foreach (var item in items)
                {
                    if (item?["ask"]?.GetValue<string>() != ask) continue;
                    moved = true;
                    var state = item["state"]?.GetValue<string>();
                    if (state == "started") return Whole(item["eval"]!.AsValue());
                    if (state == "declined") throw new DeclinedByThePerson(ask);
                    if (state == "failed") throw new EvalFailed(ask, item["why"]?.GetValue<string>() ?? "the evaluation did not start");
                }
            }
            idle = moved ? 0 : idle + 1;
        }
        throw new TimeoutException(ask + ": no answer on the sheet");
    }
}
