// -----------------------------------------------------------------------------
// Side.cs
//
// Defines `Side` — whether an order rests on the buy (bid) or sell (ask) side.
//
// A small, shared vocabulary type used throughout decoding and the order book.
// Because an order id is only unique within a (symbol, side) pair, the side is
// part of an order's identity — so it earns a type-safe enum rather than having
// raw 'B'/'S' bytes passed around the codebase.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// The side of the book an order rests on.
/// </summary>
/// <remarks>
/// In the wire format the side is encoded as a single ASCII byte:
/// <c>'B'</c> for a buy order (bid) and <c>'S'</c> for a sell order (ask).
/// An order id is only unique within a single (symbol, side) pair, so the
/// side is part of an order's identity.
/// </remarks>
public enum Side : byte
{
    /// <summary>A buy order, a.k.a. a bid. Encoded as <c>'B'</c>.</summary>
    Buy,

    /// <summary>A sell order, a.k.a. an ask. Encoded as <c>'S'</c>.</summary>
    Sell,
}
