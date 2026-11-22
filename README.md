# Bentrix SDK for .NET

The .NET client of [Bentrix](https://bentrix.net) — charts, a replay laboratory and trading through your own MetaTrader — for a program or an agent.

It finds the Bentrix running on this computer through the agents folder Bentrix writes at every start, pairs with it under the name you give — the name you see in ☰ › Settings › **Agents**, where the pairing's permissions are set — and calls every tool of Bentrix's capability catalogue over MCP. Nothing is typed: no address, no key.

The client is generated from the catalogue of the Bentrix release it came with, and checks when it connects that the running Bentrix serves the same major version.

## What you need

The .NET SDK (6 or later), and Bentrix open on the same computer.

## Install

The client is the project `mcp/Bentrix.Mcp.csproj`. Make a console project beside a clone of this repository and reference it:

```bash
dotnet new console -o MyAgent
cd MyAgent
dotnet add reference ../bentrix-dotnet/mcp/Bentrix.Mcp.csproj
```

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

## The engine in your own process

`src/Bentrix` and its Gym (the in-process engine) use Bentrix's engine library, which is not part of this repository.

## Documentation

Every method, its arguments, the permission it needs and what it answers: **https://bentrix.net/en/docs/sdk/csharp/**

## Licence

MIT — see [LICENSE](LICENSE). The licence covers this source code; Bentrix itself is not covered by it.
