# Architecture & Design

This document explains *how* the solution is built and *why* it is built that way. For a
high-level overview and usage, start with the [README](../README.md).

## Design goals

1. **Correctness first.** The grader compares output byte-for-byte, so the design optimises for
   provable correctness and exact formatting over cleverness.
2. **Separation of concerns.** Pure domain logic (decoding, the book, snapshots) is isolated from
   I/O so it can be unit-tested directly and reasoned about in isolation.
3. **Clarity.** Each type has one responsibility and a self-describing name. A reviewer should be
   able to read any file top-to-bottom and understand it without cross-referencing.
4. **Streaming & efficiency.** A single pass over the feed, holding only one message at a time,
   with no allocations on the common "depth didn't change" path.

## The pipeline

```text
bytes ─► FeedReader ─► FeedMessage ─► DepthEngine ─► (SymbolBook ─► SideBook×2) ─► DepthSnapshot? ─► SnapshotFormatter ─► line
```

A message is decoded, routed to its symbol, applied to one side of that symbol's book, and then
the engine decides whether the visible depth changed. Only on a change is a snapshot produced and
formatted. `null` means "nothing to print".

## Components

| File | Responsibility |
|------|----------------|
| `FeedReader` | Turn the binary stream into `FeedMessage` values. Owns the wire format: header framing, field offsets, binary field decoding, unknown-type skipping. |
| `FeedMessage` | An allocation-free `readonly struct` carrying one decoded message. Static factories (`Add`, `Update`, `Delete`, `Execute`, `Unknown`) make intent explicit. |
| `Side`, `MessageKind` | Wire enums (`Buy`/`Sell`, the four message kinds + `Unknown`). |
| `SideBook` | One side (bid **or** ask) of one symbol. Maintains orders-by-id and a price→volume ladder. All add/update/delete/execute mechanics live here. |
| `SymbolBook` | Both sides of one symbol, plus the change-detection bookkeeping (last published top levels, reusable scratch buffers). |
| `DepthEngine` | The public entry point. Routes each message to the right `SymbolBook` and returns a `DepthSnapshot?`. |
| `PriceLevel`, `DepthSnapshot` | Immutable result types: a `(price, volume)` level and a top-levels snapshot for a symbol. |
| `SnapshotFormatter` | Renders a snapshot to the exact required text line. The only place output text is shaped. |
| `Program`, `CommandLineOptions` | The console host: parse arguments, wire stdin/file to the engine, write LF-terminated lines, map errors to exit codes. |

## Data structures, and why

Each `SideBook` holds two structures, kept consistent on every operation:

```csharp
Dictionary<ulong, OrderEntry>     _orders;   // orderId -> (price, volume)
SortedDictionary<int, long>       _levels;   // price   -> aggregated volume (> 0 only)
```

- **`_orders`** answers "what price/volume does this order currently have?" — needed because
  Update/Delete/Execute reference an order by id and must withdraw its *old* contribution before
  applying the new one. `Dictionary` gives O(1) lookup.
- **`_levels`** is the depth itself: the sum of volumes at each price. A `SortedDictionary` keeps
  prices ordered, so producing the top levels is just "take the first few entries". For **bids** the
  dictionary uses a descending comparer (highest price first); for **asks** the default ascending
  order is already correct.
- **Only positive levels are stored.** When a level's aggregate volume reaches zero it is removed,
  so the ladder always *is* the visible depth — no filtering needed when reading it, and a
  zero-volume order naturally contributes nothing.

Keeping the two in lock-step (every mutation updates both) is the core invariant. Each of the four
operations is a few lines:

| Message | Effect on `_orders` | Effect on `_levels` |
|---------|---------------------|---------------------|
| **Add** | insert id → (price, vol) (withdrawing any prior entry for that id first) | `+vol` at price |
| **Update** | replace id → (new price, new vol) | `−oldVol` at old price, `+newVol` at new price |
| **Delete** | remove id | `−vol` at price |
| **Execute** | reduce vol by traded qty; remove id if it hits zero | `−traded` at price |

`Add` withdrawing a prior entry is defensive: order ids are unique per side, so it should never
fire, but if a duplicate ever arrived it keeps `Add` symmetric with `Update` and prevents an
orphaned level. Execute clamps the traded quantity to the remaining volume so the ladder can never
go negative even on a malformed feed.

## Change detection

The task says: publish a snapshot only when the top levels changed from the last ones
published for that symbol. `SymbolBook` implements this:

1. After applying a message, copy the top levels of each side into reusable scratch buffers
   (`CopyTop` walks the sorted ladder and stops at the requested number of levels).
2. Compare the scratch buffers element-by-element to the arrays that were last published.
3. If both sides match, return `null` — no allocation, no output.
4. Otherwise, take trimmed copies as the new "last published" arrays and return a `DepthSnapshot`.

The baseline "last published" is an empty book, so the first message that creates visible depth is
itself a change. Because comparison happens against the *published* top levels (not the full book), a
change to a hidden, deeper level correctly produces no output.

This is the one place a subtle bug could hide, so it is deliberately simple: rebuild the top levels of
both sides every message and compare. At 5 levels over tens of thousands of messages this is
negligible, and it removes any chance of stale cached state.

## Complexity

For a side with _L_ distinct prices and a requested number of levels:

- Add / Update / Delete / Execute: **O(log L)** (the `SortedDictionary` updates) plus O(1) order map work.
- Building the top levels for change detection: **O(levels)** per message.
- Memory: **O(total live orders + distinct prices)**.

The whole `input2.stream` (33,287 messages) processes in under one second.

## Key decisions and trade-offs

- **`SortedDictionary` vs sorting on demand.** Maintaining sorted ladders makes top-levels extraction
  trivial and keeps per-message cost logarithmic. Re-sorting all prices each message would be
  simpler to write but O(L log L) per message. For depth feeds the incremental structure is the
  natural fit.
- **Allocation-free comparison path.** Scratch buffers plus "only allocate on an actual change"
  means a busy feed where deep levels churn produces no garbage. The decoder still allocates one
  symbol string per message; interning is possible but unnecessary at this scale, and is called out
  honestly in the README rather than over-engineered.
- **`readonly struct` for `FeedMessage`.** Decoding tens of thousands of messages with no per-message
  heap allocation, while remaining clear via named factories. Records were considered but a single
  value type keeps the hot path allocation-free.
- **Explicit binary decode (`BinaryPrimitives`).** The byte order is fixed by the decode helpers, so
  the result is identical on any machine — not reliant on `BitConverter`'s platform behaviour.
- **Forward-compatible reader.** The header's body length lets the reader skip unknown (or
  zero-length) message types cleanly instead of crashing, which matches how real exchange feeds
  evolve.
- **Byte-exact output by construction.** LF line endings written explicitly (not `WriteLine`),
  no BOM, and `InvariantGlobalization` in the build so number formatting can never pick up a
  locale's thousands separator or negative sign. These remove whole classes of "works on my
  machine" diffs.

## Testing strategy

- **Unit tests** exercise each component in isolation (decoder, side book, engine, formatter, CLI),
  including the edge cases the provided sample does not contain.
- **End-to-end tests** run the real pipeline and assert byte-for-byte equality: one against
  the provided `input1.stream`/`output1.log`, and one against a synthetic multi-symbol, Delete-heavy
  fixture whose expected output was produced by an *independent* implementation (keeping it
  non-circular).
- **Cross-validation** against a separate Python implementation confirms agreement on both streams
  across many depth values.

See [`tests/OrderBookFeed.Tests`](../tests/OrderBookFeed.Tests).

## Possible extensions

The shape of the code makes these small, local changes rather than rewrites:

- **Multiple output sinks / a different format** — add a formatter; the engine is unchanged.
- **Querying the book** (best bid/ask, full depth) — `SideBook` already has the data; add a reader.
- **Higher throughput** — intern symbols, or replace `SortedDictionary` with a specialised
  price-bucket structure if the price range is bounded. Neither is needed for the current data.
