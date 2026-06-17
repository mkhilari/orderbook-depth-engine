// -----------------------------------------------------------------------------
// DepthSnapshot.cs
//
// Defines `DepthSnapshot` — an immutable price-depth snapshot (the top levels) for one symbol.
//
// This is what the engine publishes and the formatter renders. Being immutable, a
// published snapshot can be safely retained as the "last published" baseline that
// the next message is compared against during change detection.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// An immutable price-depth snapshot (the top levels) for a single symbol at the moment a
/// message changed its visible depth.
/// </summary>
/// <remarks>
/// <see cref="Bids"/> are ordered most-competitive-first (highest price first) and
/// <see cref="Asks"/> are likewise most-competitive-first (lowest price first). Each list
/// contains at most as many levels as were requested.
/// </remarks>
public sealed class DepthSnapshot
{
    private readonly PriceLevel[] _bids;
    private readonly PriceLevel[] _asks;

    internal DepthSnapshot(uint sequenceNumber, string symbol, PriceLevel[] bids, PriceLevel[] asks)
    {
        SequenceNumber = sequenceNumber;
        Symbol = symbol;
        _bids = bids;
        _asks = asks;
    }

    /// <summary>The sequence number of the message that produced this snapshot.</summary>
    public uint SequenceNumber { get; }

    /// <summary>The symbol this snapshot describes.</summary>
    public string Symbol { get; }

    /// <summary>The bid levels, highest price first.</summary>
    public IReadOnlyList<PriceLevel> Bids => _bids;

    /// <summary>The ask levels, lowest price first.</summary>
    public IReadOnlyList<PriceLevel> Asks => _asks;
}
