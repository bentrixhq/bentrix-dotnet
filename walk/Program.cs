// **The .NET binding's walk on the CI tape**: the engine through the C ABI - the tape made, a session
// opened, the workbench armed and advanced as Play would, a market order filled, the book read, a
// refusal raised in its words - and an episode of the Gym from seed 5 with a fixed list of actions,
// printed as JSON (its start, every reward and the ledger hash at every step) so the same episode run
// by another language's binding can be set beside it, value for value.
//
//   dotnet run --project sdk/csharp/walk -- <empty directory>    (the C ABI's library on its path)

using System.Text.Json;
using Bentrix;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: walk <empty directory>");
    return 2;
}

void Check(bool held, string what)
{
    if (!held)
    {
        throw new Exception($"FAILED: {what}");
    }
}

Console.WriteLine($"bentrix {Local.Version}");
var (ws, store) = Local.MakeTape(args[0]);
using var local = new Local(ws, store);
try
{
    using var again = new Local(ws, store);
    Check(false, "a second session on the held workspace was not refused");
}
catch (BentrixException e)
{
    Console.WriteLine($"a second session refused: {e.Message}");
}
var bench = local.Call("get_state")!["laboratories"]![0]!["benches"]![0]!["uri"]!.GetValue<string>();
local.Arm(bench);
var from = local.Call("replay_step", new { bench, unit = "tick", n = 1 })!["cursor_ms"]!.GetValue<long>();
var reached = local.Advance(bench, from + 30L * 3_600_000);
Check(reached >= from + 30L * 3_600_000, "the workbench did not advance");
local.Call("order_market", new { bench, symbol = "XAUUSD", side = "buy", volume = 0.01 });
local.Advance(bench, reached + 60_000);
var positions = local.Book(bench)!["positions"]!.AsArray();
Check(positions.Count == 1 && positions[0]!["side"]!.GetValue<string>() == "buy", $"the order did not fill: {positions.ToJsonString()}");
try
{
    local.Call("no_such_tool");
    Check(false, "a refusal answered as a success");
}
catch (BentrixException e)
{
    Check(e.Message.Contains("no_such_tool"), "a refusal did not say its tool");
}
Check(Local.IntervalMs("H1") == 3_600_000, "H1 is not an hour");

using var env = new TradingEnv(local, bench, interval: "H1", window: 8, episode: "window", episodeBars: 12);
var actions = new[] { 1, 1, 2, 0, 2, 2, 1, 0, 1, 2, 0, 1 };
var (obs, info) = env.Reset(seed: 5);
Check(obs.Market.GetLength(0) == 8 && obs.Account.Length == 3, "the observation is not shaped as declared");
var rewards = new List<double>();
var hashes = new List<string> { info["ledger_hash"]!.GetValue<string>() };
foreach (var a in actions)
{
    var s = env.Step(a);
    rewards.Add(s.Reward);
    hashes.Add(s.Info["ledger_hash"]!.GetValue<string>());
    if (s.Terminated || s.Truncated)
    {
        break;
    }
}
Check(hashes.Distinct().Count() > 1, "the episode traded nothing");
Console.WriteLine(JsonSerializer.Serialize(new { start_ms = info["start_ms"]!.GetValue<long>(), rewards, hashes }));
Console.WriteLine("PASS");
return 0;
