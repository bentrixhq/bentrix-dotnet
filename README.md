# Bentrix SDK for .NET

The .NET client of [Bentrix](https://bentrix.net) — charts, a replay laboratory and trading through your own MetaTrader — for a program or an agent.

It finds the Bentrix running on this computer through the agents folder Bentrix writes at every start, pairs with it under the name you give — the name you see in ☰ › Settings › **Agents**, where the pairing's permissions are set — and calls every tool of Bentrix's capability catalogue over MCP. Nothing is typed: no address, no key.

The client is generated from the catalogue of the Bentrix release it came with, and checks when it connects that the running Bentrix serves the same major version.

## What you need

The .NET SDK 8 or later, and Bentrix open on the same computer.

## Install

Everything is the one package `Bentrix` on NuGet - the client of a running Bentrix and the engine in your own process:

```bash
dotnet new console -o MyAgent
cd MyAgent
dotnet add package Bentrix
```

Or from a clone of this repository, by reference: `dotnet add reference ../bentrix-dotnet/src/Bentrix/Bentrix.csproj`.

## Connect

```csharp
using Bentrix.Mcp;

using var b = await BentrixClient.ConnectAsync("my-agent");
var state = await b.Workspace.GetState();
var bench = state!["laboratories"]![0]!["benches"]![0]!["uri"]!.GetValue<string>();
var p = bench.Replace("bentrix://", "").Split('/');
var book = await b.Trade.Book(p[0], p[1], p[3]);
Console.WriteLine(book!["positions"]!.AsArray().Count);
```

A workbench as one object: its orders, its book, its clock.

```csharp
var bench = await Bench.Open(b);
var answer = await bench.Buy(0.1, sl: 2300.0);
Console.WriteLine(answer.Ok ? answer.Outcome : answer.Reason);
await bench.Step(interval: "H1");
Console.WriteLine((await bench.Positions()).Count);
```

## The engine in your own process

The same package's `Bentrix.Local` and the Gym's `TradingEnv` run Bentrix's engine inside your program, with no window.

The engine is Bentrix's own library, `libbentrix`, which Bentrix's installer puts in its folder (`%LOCALAPPDATA%\Programs\BENTRIX\libbentrix.dll` on Windows) and the binding finds there; `BENTRIX_FFI` (the file) or `BENTRIX_FFI_DIR` (its folder) names another. It runs for a computer signed in to Bentrix with Plus or Pro; with Free, connect to the running application as above.

## Documentation

Every method, its arguments, the permission it needs and what it answers: **https://bentrix.net/en/docs/sdk/csharp/**

## Licence

MIT — see [LICENSE](https://github.com/bentrixhq/bentrix-dotnet/blob/main/LICENSE). It covers this source code only: Bentrix's engine library (`libbentrix`) and the Bentrix application are Bentrix's own, under their own terms.
