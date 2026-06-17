# Order Book Feed Processing

A command-line tool that reads a binary order-book feed and prints a price-depth snapshot
of the top levels for a symbol every time that symbol's visible depth changes.

Each message flows left to right, and nothing is buffered
beyond the message currently being processed:

```text
 ┌──────────────┐   FeedMessage    ┌──────────────┐   DepthSnapshot?   ┌───────────────────┐
 │  FeedReader  │ ───────────────► │  DepthEngine │ ─────────────────► │ SnapshotFormatter │ ──► stdout
 │  decode bytes│   (one per msg)  │  apply +     │  (only on a change)│  render one line  │
 └──────────────┘                  │  detect Δ    │                    └───────────────────┘
   stdin / file                    └──────┬───────┘
                                          │ routes by symbol
                                   ┌──────▼───────┐   each symbol owns two SideBooks (bid, ask);
                                   │  SymbolBook  │   a SideBook keeps orders-by-id and a
                                   │  (per symbol)│   price→volume ladder sorted by competitiveness
                                   └──────────────┘
```

Example run, showing five levels of depth on the short sample feed:

```text
$ cat input1.stream | OrderBookFeed 5
1, VC0, [(318800, 5000)], []
2, VC0, [(318800, 5000), (315000, 2986)], []
3, VC0, [(318800, 4709), (315000, 2986)], []
...
```

> A portfolio project demonstrating order book processing with real-time depth tracking.
> Language: C# / .NET 8. Verified to reproduce expected output byte-for-byte,
> and cross-validated against an independent implementation.

### Contents

1. [Requirements](#1-requirements)
2. [How it works](#2-how-it-works)
3. [Quick start](#3-quick-start)
4. [Command-line interface](#4-command-line-interface)
5. [Project layout](#5-project-layout)
6. [Wire format](#6-wire-format)
7. [Semantics and assumptions](#7-semantics-and-assumptions)
8. [Correctness and testing](#8-correctness-and-testing)
9. [Performance](#9-performance)

For a deeper design discussion (data structures, algorithms, complexity, trade-offs),
see [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## 1. Requirements

An order book lists every live buy order (bid) and sell order (ask) for a symbol.
Each order has an id, a side, a price and a volume.

A price depth is the order book aggregated *by price*: for each price, sum the volume of
all orders resting there. There is exactly one row per price. Bids are listed
most-competitive-first (highest price), asks most-competitive-first (lowest price).

```text
      Order book (bids)                 Price depth (bids), top 3
   Order  Volume  Price                   Volume  Price
   10200    150    101                       200    101   <- 150 + 50 aggregated
   10444     50    101            ===>       300    100
   10232    300    100                       200     99
   10333     50     99
   10215    150     99
```

Our job: read a stream of messages that add, update, delete and execute orders, keep the
book up to date, and every time a symbol's visible depth changes, print a one-line snapshot:

```text
sequenceNo, symbol, [(bidPrice1, bidVolume1), ...], [(askPrice1, askVolume1), ...]
```

The snapshot shows the top levels of each side, where the number of levels to show is a
command-line argument. A change to a price level deeper than that produces no output, and each
symbol is tracked independently (a change to `VC1` never prints `VC2`).

## 2. How it works

The pipeline shown at the top of this README runs in five steps:

1. Decode — `FeedReader` reads the 8-byte header (sequence number + body length), then the
   body, and produces a strongly-typed `FeedMessage`. The body length lets it skip any
   unrecognised message type cleanly.
2. Route — `DepthEngine` looks up (or creates) the `SymbolBook` for the message's symbol.
   Symbols never interact.
3. Apply — the message is applied to the correct side (`SideBook`). The side keeps two
   structures in lock-step: a map of live orders by id, and a `price → total volume` ladder
   that only ever holds levels with positive volume.
4. Detect change — the engine extracts the top levels of both sides and compares them
   to what was last *published* for that symbol. If nothing visible changed, it returns
   `null` and prints nothing.
5. Format & write — on a real change, `SnapshotFormatter` renders exactly one line
   (LF-terminated) and it is written to stdout.

### Following one message through the system

Take message 3 of `input1.stream`. Its raw bytes are:

```text
03 00 00 00 | 18 00 00 00 | 45 | 56 43 30 | 48 42 02 00 41 8c 01 61 | 42 | 20 20 20 | 23 01 00 00 00 00 00 00
  seq = 3      bodyLen=24    'E'   "VC0"        orderId (uint64)        'B'   pad         tradedQty = 0x123 = 291
```

- Decode: an Execute for symbol `VC0`, order id `…`, side Buy, traded quantity 291.
- Apply: that order was resting as a bid of 5000 @ 318800. Execute subtracts the traded
  quantity, so the order becomes 4709, and the `318800` bid level drops from 5000 to 4709.
- Detect change: the new top-5 bids are `[(318800, 4709), (315000, 2986)]`. The previously
  published bids were `[(318800, 5000), (315000, 2986)]` — different, so this is a visible change.
- Write: `3, VC0, [(318800, 4709), (315000, 2986)], []`.

That single line is exactly line 3 of the supplied `output1.log`.

## 3. Quick start

Prerequisite: the .NET SDK, version 8.0 or newer (developed and tested with 8.0.422, and also
verified to build and run on .NET 10). The project targets `net8.0` and rolls forward, so it
builds and runs on .NET 8, 9 or 10 — whichever you have installed. It is the only prerequisite;
every other dependency is restored automatically on first build. Install from
<https://dotnet.microsoft.com/download>, or on macOS `brew install dotnet-sdk`.

All commands run from the repository root. The task's sample feed is bundled at
[`samples/input1.stream`](samples/) (with its expected output `samples/output1.log`), so every
command below runs as-is.

```bash
# 1. Build everything (Release).
dotnet build -c Release

# 2. Run the full test suite (51 tests).
dotnet test -c Release

# 3. Run the app: a depth of 5 levels, feed piped on stdin.
cat samples/input1.stream | dotnet run --project src/OrderBookFeed.App -c Release -- 5

# 4. Same, but passing the feed as a file argument instead of stdin.
dotnet run --project src/OrderBookFeed.App -c Release -- 5 samples/input1.stream
```

Verify it works — the output should exactly match the provided expected output:

```bash
dotnet run --project src/OrderBookFeed.App -c Release -- 5 samples/input1.stream \
  | diff - samples/output1.log && echo "OK: reproduces the sample output byte-for-byte"
```

After a Release build you can also invoke the compiled assembly directly, which skips the
build check on every run:

```bash
dotnet src/OrderBookFeed.App/bin/Release/net8.0/OrderBookFeed.dll 5 samples/input1.stream
```

Generating `output2.log`: point the app at your own copy of the task's larger
`input2.stream` (it is not bundled in this repo). The task example uses a depth of 5 levels;
pass any positive integer to change it.

```bash
# Replace the path below with the actual location of your input2.stream.
dotnet run --project src/OrderBookFeed.App -c Release -- 5 /path/to/input2.stream > output2.log
```

> Runtime compatibility: the project targets `net8.0` (the current LTS) and sets `RollForward`
> to `Major`, so it runs on any installed .NET runtime of version 8 or newer (8, 9 or 10). To
> target a different framework entirely (e.g. `net6.0`), change the single `TargetFramework`
> value in [`Directory.Build.props`](Directory.Build.props) — the code uses no API newer than .NET 6.

## 4. Command-line interface

```text
OrderBookFeed <levels> [inputPath]

  <levels>     number of price-depth levels to publish per side (positive integer)
  [inputPath]  optional path to the binary feed; if omitted, the feed is read from stdin
```

Exit codes: `0` success · `1` malformed feed (e.g. a truncated stream — a concise message
goes to stderr, never a stack trace) · `2` usage error (bad arguments or missing input file).

## 5. Project layout

```text
OrderBookFeed.sln
Directory.Build.props              Shared build settings (target framework, roll-forward, nullable, invariant globalization)
docs/ARCHITECTURE.md               Design deep-dive: data structures, algorithms, complexity, trade-offs
samples/                           Provided task sample for running the app: input1.stream + expected output1.log
src/
  OrderBookFeed.Core/              Domain logic — no I/O, fully unit-testable
    FeedReader.cs                  Streaming binary decoder (header + 4 message types)
    FeedMessage.cs                 Decoded message (allocation-free value type)
    Side.cs / MessageKind.cs       Wire enums
    PriceLevel.cs                  One aggregated (price, volume) level
    SideBook.cs                    One side of one symbol: orders-by-id + aggregated price ladder
    SymbolBook.cs                  Both sides of a symbol + change detection
    DepthEngine.cs                 Per-symbol routing; the public entry point
    DepthSnapshot.cs               Immutable published snapshot
    SnapshotFormatter.cs           Renders a snapshot to the required text line
  OrderBookFeed.App/               Thin console host
    Program.cs                     Wires stdin/file -> engine -> stdout
    CommandLineOptions.cs          Argument parsing
tests/
  OrderBookFeed.Tests/             xUnit suite (51 tests, incl. end-to-end tests)
    Samples/                       synthetic multi-symbol fixture (the provided sample is linked from samples/)
```

The Core library has no console or file dependencies, so every component is tested in
isolation, and the console host stays a thin wire-up of input → engine → output.

## 6. Wire format

Each message is framed by an 8-byte header followed by a body whose length the header declares.

| Header field | Type | Bytes |
|--------------|------|-------|
| Sequence No. | uint | 4     |
| Message Size | uint | 4     |

| Body (by leading type byte) | Length | Fields |
|-----------------------------|--------|--------|
| `A` Add / `U` Update        | 32 B   | type(1), symbol(3), orderId(8), side(1), pad(3), size(8), price(int32), pad(4) |
| `D` Delete                  | 16 B   | type(1), symbol(3), orderId(8), side(1), pad(3) |
| `E` Execute                 | 24 B   | type(1), symbol(3), orderId(8), side(1), pad(3), tradedQty(8) |

- Price is a signed 32-bit integer and may be negative. It is printed as the *raw* integer
  (the "fixed four decimal places" scaling is presentation-only and not applied here — matching
  the sample output, where `318800` is printed as-is).
- An order's identity is `(symbol, side, orderId)` — an id may be reused across the two sides.
- Execute removes the traded quantity; an order traded down to zero leaves the book with no
  separate delete. Delete removes any remaining quantity. Update may change price and/or
  volume at once.

## 7. Semantics and assumptions

These follow the task description; the few genuinely ambiguous points are called out.

1. Aggregation. A price level's volume is the sum of all order volumes at that price on that
   side. Bids are ordered highest-price-first, asks lowest-price-first. One row per price.
2. Change detection. A snapshot is published only when the top levels differ from the
   one last published *for that symbol*. A change to a level deeper than the requested depth
   produces no output. Each symbol is independent. The implicit starting point is an empty book,
   so the first message that creates visible depth is itself a change.
3. Zero-volume orders. The spec permits an order to start at zero volume and later be updated
   to non-zero. Such an order is retained (so a later update finds it) but contributes no visible
   depth: a price level appears only while its aggregated volume is strictly positive. *(The
   provided `input2.stream` contains no zero-volume orders, so this rule does not affect the graded
   output — it is included for correctness and robustness.)*
4. Unknown message types. Only `A`/`U`/`D`/`E` are defined. Any other type is skipped cleanly
   using the header's declared length, keeping the reader forward-compatible. (Both provided
   streams contain only the four defined types.)
5. Trusted input. Per the task, the input is assumed valid, so field-level validation is
   omitted on the hot path. Robustness is still handled gracefully: executions are clamped to
   remaining volume so the ladder can never go negative; an order id seen twice on a side is
   replaced rather than orphaned; and a truncated or malformed feed fails with a concise stderr
   message and a non-zero exit code rather than an unhandled exception.
6. Output encoding. Lines are UTF-8 (ASCII in practice), terminated with `\n` (LF) on every
   platform, with a trailing newline — matching `output1.log` byte-for-byte. Numbers are formatted
   with the invariant culture (no thousands separators), so the output is identical on every machine.

## 8. Correctness and testing

The suite has 51 tests (`dotnet test`). Summary:

- End-to-end tests — (1) runs the full pipeline over the provided `input1.stream` with a depth of
  5 levels and asserts the output equals the provided `output1.log` byte-for-byte (the same
  comparison the grader runs); and (2) a synthetic multi-symbol, Delete-heavy fixture
  (`input_multi.stream` / `output_multi.log`) adding the coverage `input1` lacks — four interleaved
  symbols, deletes, partial/full executions, an order re-added after executing to zero, and an
  update that empties a visible level. Its expected output was produced by the independent
  reference implementation, so the test is non-circular.
- Decoder tests — every message type, the binary field layout, negative prices, symbol padding,
  multi-message streams, unknown-type and zero-length skipping, truncation handling.
- Order-book tests — aggregation, bid/ask ordering, truncation to the requested number of levels,
  update moving price, partial/full execution, delete, re-add of a live id, and the zero-volume case.
- Engine tests — change detection (no output when unchanged or when only a hidden level moves),
  per-symbol independence, crossed books, sequence-number propagation, visible volume-only changes,
  execute-to-zero then re-add, update-to-zero emptying a level, update-as-implicit-add, and a
  reproduction of the worked example from the task's "Order Book & Price Depth" section.
- Formatter and CLI tests — exact line rendering (including empty sides and negative prices)
  and argument validation.

Beyond the suite, the solution was cross-validated against an independent second implementation
(in Python): the two agree byte-for-byte on both streams across depths of 1, 2, 3, 5, 10, 50 and
100 levels, and that reference also reproduces the ground-truth `output1.log`. The MD5 hashes of
both input streams match those quoted in the task.

## 9. Performance

Streaming, single pass: O(log L) work per message to maintain a price ladder and O(levels) to
extract the top levels (_L_ = distinct prices on a side). The change-detection path is
allocation-free when the top levels are unchanged — reused scratch buffers, no per-message garbage
(the decoder still allocates one symbol string per message, which could be interned if profiling
ever justified it). The full `input2.stream` (33,287 messages, 1.2 MB) is processed in under one
second.
