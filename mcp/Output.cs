// A model's output of any kind on the chart: the output_spec document built by hand for the eleven kinds beside
// a path (a path is Forecast's) - scalar, classes, quantiles, rank, position, weights, event, levels, action, text,
// series - checked as Bentrix checks it, a value outside its declared range refused in words and never clamped, and
// put through output_put; and the person's meaning (output_meaning) set through output_meaning_set, never in a model's
// file. An output never names its arrival: Bentrix stamps put_ms.
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bentrix.Mcp;

/// <summary>A refusal in Bentrix's words, raised before anything is sent.</summary>
public sealed class OutputRefused : ArgumentException
{
    public OutputRefused(string m) : base(m) { }
}

/// <summary>One output's document: its kind, its values as the document writes them, and the kind's own fields.</summary>
public sealed class Output
{
    /// <summary>The twelve kinds, as the document writes them.</summary>
    public static readonly string[] Kinds = { "path", "scalar", "classes", "quantiles", "rank", "position", "weights", "event", "levels", "action", "text", "series" };
    /// <summary>The meaning's closed words.</summary>
    public static readonly string[] MeaningWords = { "buy", "sell", "nothing", "up", "down", "flat", "in", "out", "close" };
    /// <summary>An action's intents.</summary>
    public static readonly string[] Intents = { "buy", "sell", "close_buys", "close_sells", "nothing" };

    public readonly string Kind, Model, Name, Symbol;
    public readonly long AnchorMs;
    public readonly List<JsonObject> Values = new();
    /// <summary>range, units, horizon, names, levels, universe, markets, long_only, exclusive, softmax, persistent, gross, note.</summary>
    public readonly JsonObject Fields = new();
    public int Epoch;
    public OutputMeaning? SuggestedMeaning;

    Output(string kind, string model, string name, string symbol, long anchorMs)
    {
        Kind = kind; Model = model; Name = name; Symbol = symbol; AnchorMs = anchorMs;
    }

    Output Value(JsonObject v) { Values.Add(v); return this; }
    Output With(string k, JsonNode? v) { Fields[k] = v; return this; }

    static JsonObject Map(IDictionary<string, double> m)
    {
        var o = new JsonObject();
        foreach (var (k, v) in m) o[k] = v;
        return o;
    }

    static JsonArray Strings(IEnumerable<string> xs) => new(xs.Select(x => (JsonNode?)x).ToArray());

    /// <summary>A number per step; range its declared bounds, or null.</summary>
    public static Output Scalar(string model, string name, string symbol, long anchorMs, (double Lo, double Hi)? range, params double[] vs)
    {
        var o = new Output("scalar", model, name, symbol, anchorMs);
        foreach (var v in vs) o.Value(new JsonObject { ["value"] = v });
        return range is { } r ? o.With("range", new JsonArray(r.Lo, r.Hi)) : o;
    }

    /// <summary>Probabilities over named classes, one map per step.</summary>
    public static Output Classes(string model, string name, string symbol, long anchorMs, string[] names, bool exclusive, params IDictionary<string, double>[] steps)
    {
        var o = new Output("classes", model, name, symbol, anchorMs).With("names", Strings(names)).With("exclusive", exclusive);
        foreach (var p in steps) o.Value(new JsonObject { ["p"] = Map(p) });
        return o;
    }

    /// <summary>Quantiles at levels, one map per step keyed as the document writes a level ("0.1").</summary>
    public static Output Quantiles(string model, string name, string symbol, long anchorMs, double[] levels, params IDictionary<string, double>[] steps)
    {
        var o = new Output("quantiles", model, name, symbol, anchorMs).With("levels", new JsonArray(levels.Select(x => (JsonNode?)x).ToArray()));
        foreach (var q in steps) o.Value(new JsonObject { ["q"] = Map(q) });
        return o;
    }

    /// <summary>A rank in 0..1 among universe markets.</summary>
    public static Output Rank(string model, string name, string symbol, long anchorMs, int universe, double value) =>
        new Output("rank", model, name, symbol, anchorMs).With("universe", universe).Value(new JsonObject { ["value"] = value });

    /// <summary>A target position in units (fraction, lots, ...).</summary>
    public static Output Position(string model, string name, string symbol, long anchorMs, string units, double target) =>
        new Output("position", model, name, symbol, anchorMs).With("units", units).Value(new JsonObject { ["target"] = target });

    /// <summary>Weights across markets.</summary>
    public static Output Weights(string model, string name, string symbol, long anchorMs, bool longOnly, IDictionary<string, double> w) =>
        new Output("weights", model, name, symbol, anchorMs).With("long_only", longOnly).With("markets", Strings(w.Keys)).Value(new JsonObject { ["w"] = Map(w) });

    /// <summary>An event's score in 0..1, expected within withinSteps (0 for none).</summary>
    public static Output Event(string model, string name, string symbol, long anchorMs, double score, int withinSteps = 0)
    {
        var v = new JsonObject { ["score"] = score };
        if (withinSteps > 0) v["within_steps"] = withinSteps;
        return new Output("event", model, name, symbol, anchorMs).Value(v);
    }

    /// <summary>Named price levels (stop, target, ...).</summary>
    public static Output Levels(string model, string name, string symbol, long anchorMs, IDictionary<string, double> levels) =>
        new Output("levels", model, name, symbol, anchorMs).Value(new JsonObject { ["levels"] = Map(levels) });

    /// <summary>An intent, with a size where it has one.</summary>
    public static Output Action(string model, string name, string symbol, long anchorMs, string intent, double? size = null)
    {
        var v = new JsonObject { ["intent"] = intent };
        if (size is { } s) v["size"] = s;
        return new Output("action", model, name, symbol, anchorMs).Value(v);
    }

    /// <summary>A sentence.</summary>
    public static Output Text(string model, string name, string symbol, long anchorMs, string text) =>
        new Output("text", model, name, symbol, anchorMs).Value(new JsonObject { ["text"] = text });

    /// <summary>Named lines, one map per step.</summary>
    public static Output Series(string model, string name, string symbol, long anchorMs, string[] names, params IDictionary<string, double>[] steps)
    {
        var o = new Output("series", model, name, symbol, anchorMs).With("names", Strings(names));
        foreach (var l in steps) o.Value(new JsonObject { ["lines"] = Map(l) });
        return o;
    }

    /// <summary>The horizon: bars of the anchor's interval.</summary>
    public Output Steps(int n) => With("horizon", new JsonObject { ["steps"] = n });
    /// <summary>The horizon in business days.</summary>
    public Output BusinessDays(int n) => With("horizon", new JsonObject { ["business_days"] = n });
    /// <summary>Held until "levels" or "next_output".</summary>
    public Output Until(string what) => With("horizon", new JsonObject { ["until"] = what });

    internal static string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void InRange(string field, JsonNode? n, (double Lo, double Hi)? range)
    {
        double v;
        try { v = n!.GetValue<double>(); }
        catch { throw new OutputRefused($"{field} {n} is not a number"); }
        if (!double.IsFinite(v)) throw new OutputRefused($"{field} {Fmt(v)} is not a number");
        if (range is { } r && (v < r.Lo || v > r.Hi)) throw new OutputRefused($"{field} the output answers {Fmt(r.Lo)} to {Fmt(r.Hi)}; {Fmt(v)} is outside it");
    }

    (double, double)? Range() => Fields["range"] is JsonArray a && a.Count == 2 ? (a[0]!.GetValue<double>(), a[1]!.GetValue<double>()) : null;

    static readonly Regex Label = new("^[a-z0-9_]{1,32}$");

    /// <summary>Refuses what Bentrix would refuse, in its words.</summary>
    public Output Check()
    {
        if (!Label.IsMatch(Model) || !Label.IsMatch(Name)) throw new OutputRefused("the model and the output are [a-z0-9_]{1,32}");
        if (!Kinds.Contains(Kind)) throw new OutputRefused($"{Kind} is not one of the twelve kinds");
        if (Values.Count == 0) throw new OutputRefused("values holds no value");
        var range = Range();
        var names = (Fields["names"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList() ?? new List<string>();
        var softmax = Fields["softmax"]?.GetValue<bool>() ?? false;
        for (var i = 0; i < Values.Count; i++)
        {
            var v = Values[i];
            var f = $"values[{i}]";
            switch (Kind)
            {
                case "scalar": InRange(f + ".value", v["value"], range); break;
                case "rank": InRange(f + ".value", v["value"], range ?? (0, 1)); break;
                case "event": InRange(f + ".score", v["score"], (0, 1)); break;
                case "position": InRange(f + ".target", v["target"], range); break;
                case "classes":
                    if (names.Count == 0) throw new OutputRefused("names a classes output names its classes");
                    foreach (var (k, p) in (JsonObject)v["p"]!)
                    {
                        if (!names.Contains(k)) throw new OutputRefused($"{f}.p {k} is not one of the classes");
                        if (!softmax) InRange($"{f}.p.{k}", p, (0, 1));
                    }
                    break;
                case "series":
                    if (names.Count == 0) throw new OutputRefused("names a series output names its lines");
                    break;
                case "action":
                    var intent = v["intent"]?.GetValue<string>() ?? "";
                    if (!Intents.Contains(intent)) throw new OutputRefused($"{f}.intent {intent} is not one of {string.Join(", ", Intents)}");
                    break;
                case "text":
                    var t = v["text"]?.GetValue<string>() ?? "";
                    if (t.EnumerateRunes().Count() > 2000) throw new OutputRefused($"{f}.text is longer than 2000 characters");
                    break;
            }
        }
        SuggestedMeaning?.Check(range);
        return this;
    }

    /// <summary>The output_spec document; never an arrival.</summary>
    public JsonObject Spec()
    {
        var vs = new JsonArray(Values.Select(v => (JsonNode?)v.DeepClone()).ToArray());
        var d = new JsonObject
        {
            ["schema_version"] = 1, ["model"] = Model, ["output"] = Name, ["symbol"] = Symbol, ["kind"] = Kind,
            ["anchor"] = new JsonObject { ["time_ms"] = AnchorMs }, ["values"] = vs,
        };
        foreach (var (k, v) in Fields) d[k] = v?.DeepClone();
        if (Epoch > 0) d["epoch"] = Epoch;
        if (SuggestedMeaning != null) d["suggested_meaning"] = SuggestedMeaning.Doc();
        return d;
    }

    /// <summary>output_put on the workbench bench, after Check.</summary>
    public Task<JsonNode?> Put(Caller c, string bench)
    {
        Check();
        return c("output_put", new Dictionary<string, object?> { ["bench"] = bench, ["output"] = Spec() });
    }

    /// <summary>output_put_many: several outputs at one instant.</summary>
    public static Task<JsonNode?> PutOutputs(Caller c, string bench, IEnumerable<Output> outputs, bool allOrNothing = false)
    {
        var specs = new JsonArray();
        foreach (var o in outputs) specs.Add(o.Check().Spec());
        return c("output_put_many", new Dictionary<string, object?> { ["bench"] = bench, ["outputs"] = specs, ["all_or_nothing"] = allOrNothing });
    }

    /// <summary>output_meaning_set: the person's meaning of model's output on the workbench bench.</summary>
    public static Task<JsonNode?> SetMeaning(Caller c, string bench, string model, string output, OutputMeaning m, (double Lo, double Hi)? range = null)
    {
        m.Check(range);
        return c("output_meaning_set", new Dictionary<string, object?> { ["bench"] = bench, ["model"] = model, ["output"] = output, ["meaning"] = m.Doc() });
    }
}

/// <summary>The output_meaning document: what a value means to the person.</summary>
public sealed class OutputMeaning
{
    public string Reads = "direction";
    public string? HighMeans, LowMeans;
    public double? ActAbove, ActBelow;
    public double Hysteresis;
    public Dictionary<string, string> Labels = new();

    /// <summary>high above actAbove, low below actBelow.</summary>
    public static OutputMeaning Of(string high, string? low = null, double? actAbove = null, double? actBelow = null) =>
        new() { HighMeans = high, LowMeans = low, ActAbove = actAbove, ActBelow = actBelow };

    /// <summary>Refuses a word that is not the product's, a threshold outside range, a label empty or over 24.</summary>
    public OutputMeaning Check((double Lo, double Hi)? range)
    {
        foreach (var w in new[] { HighMeans, LowMeans })
            if (w != null && !Output.MeaningWords.Contains(w)) throw new OutputRefused($"\"{w}\" is not one of the meaning's words");
        if (range is { } r)
            foreach (var t in new[] { ActAbove, ActBelow })
                if (t is { } x && (x < r.Lo || x > r.Hi)) throw new OutputRefused($"the output answers {Output.Fmt(r.Lo)} to {Output.Fmt(r.Hi)}; {Output.Fmt(x)} is outside it");
        foreach (var (k, l) in Labels)
            if (!Output.MeaningWords.Contains(k) || string.IsNullOrEmpty(l) || l.EnumerateRunes().Count() > 24)
                throw new OutputRefused($"the label \"{l}\" for {k} is not a meaning word's, or is empty or longer than 24");
        return this;
    }

    /// <summary>The document.</summary>
    public JsonObject Doc()
    {
        var d = new JsonObject { ["reads"] = Reads, ["hysteresis"] = Hysteresis };
        if (HighMeans != null) d["high_means"] = HighMeans;
        if (LowMeans != null) d["low_means"] = LowMeans;
        if (ActAbove is { } a) d["act_above"] = a;
        if (ActBelow is { } b) d["act_below"] = b;
        if (Labels.Count > 0)
        {
            var l = new JsonObject();
            foreach (var (k, v) in Labels) l[k] = v;
            d["labels"] = l;
        }
        return d;
    }
}
