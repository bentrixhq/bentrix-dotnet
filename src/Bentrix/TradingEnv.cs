// **The Gym, for .NET**: a client of Bentrix's own environment (env_make, env_reset, env_step; the
// environment is Rust's, crates/bentrix-shell/src/agenttools/env.rs), so its observations, actions,
// rewards and episodes are the ones every language reaches - Python's bentrix.gym.TradingEnv's to the
// bit. The contract is Gymnasium's: Reset(seed) -> (observation, info); Step(action) -> (observation,
// reward, terminated, truncated, info).

using System.Text.Json.Nodes;

namespace Bentrix;

/// <summary>The last closed candles as log ratios to the last close, [window, 4] (open, high, low,
/// close), and the account: side, log equity ratio to the episode's start, open profit over it.</summary>
public sealed record Observation(float[,] Market, float[] Account);

public sealed record StepResult(Observation Observation, double Reward, bool Terminated, bool Truncated, JsonObject Info);

public sealed class TradingEnv : IDisposable
{
    private readonly Local _local;

    public string Id { get; }
    public string Symbol { get; }
    public int Window { get; }

    /// <summary>An environment over <paramref name="bench"/> (env_make's arguments, its defaults when null).
    /// A laboratory workbench is rewound at each reset, its deals undone: give it one of its own.</summary>
    public TradingEnv(
        Local local,
        string bench,
        string? symbol = null,
        string interval = "H1",
        int window = 32,
        string action = "discrete",
        string reward = "dsr_dd_penalty@1",
        double lots = 0.01,
        string episode = "window",
        int episodeBars = 200,
        double? participation = null,
        double? impact = null)
    {
        _local = local;
        Window = window;
        var made = local.Call("env_make", new
        {
            bench,
            symbol,
            interval,
            window,
            action,
            reward,
            lots,
            episode,
            episode_bars = episodeBars,
            participation,
            impact,
        })!;
        Id = made["env"]!.GetValue<string>();
        Symbol = made["symbol"]!.GetValue<string>();
    }

    private Observation Parse(JsonNode obs)
    {
        var rows = obs["market"]!.AsArray();
        var market = new float[rows.Count, 4];
        for (var i = 0; i < rows.Count; i++)
        {
            for (var k = 0; k < 4; k++)
            {
                market[i, k] = (float)rows[i]![k]!.GetValue<double>();
            }
        }
        var account = obs["account"]!.AsArray().Select(v => (float)v!.GetValue<double>()).ToArray();
        return new Observation(market, account);
    }

    /// <summary>A new episode: the seed given starts the draws over; none takes the next episode.</summary>
    public (Observation Observation, JsonObject Info) Reset(ulong? seed = null)
    {
        var got = _local.Call("env_reset", new { env = Id, seed })!;
        return (Parse(got["obs"]!), got["info"]!.AsObject());
    }

    /// <summary>One action: 0, 1 or 2 (discrete), a number in [-1, 1] (continuous), or
    /// new { side, size, stop, target } (order).</summary>
    public StepResult Step(object action)
    {
        var got = _local.Call("env_step", new { env = Id, action })!;
        return new StepResult(Parse(got["obs"]!), got["reward"]!.GetValue<double>(), got["terminated"]!.GetValue<bool>(), got["truncated"]!.GetValue<bool>(), got["info"]!.AsObject());
    }

    public void Dispose()
    {
        try
        {
            _local.Call("env_close", new { env = Id });
        }
        catch (BentrixException)
        {
        }
    }
}
