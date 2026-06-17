// -----------------------------------------------------------------------------
// FeedMessage.cs
//
// Defines `FeedMessage` — one decoded feed message, as an allocation-free value type.
//
// It is the hand-off between the decoder (FeedReader) and the engine (DepthEngine),
// cleanly separating "how bytes are parsed" from "how the book is updated". Static
// factory methods (Add/Update/Delete/Execute/Unknown) make each message's intent
// obvious at call sites and in tests.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// A decoded order book feed message.
/// </summary>
/// <remarks>
/// <para>
/// This is an allocation-free value type carrying the union of fields across
/// the four message kinds. Which fields are meaningful depends on
/// <see cref="Kind"/>:
/// </para>
/// <list type="bullet">
///   <item><see cref="MessageKind.Add"/> / <see cref="MessageKind.Update"/>:
///         <see cref="Symbol"/>, <see cref="OrderId"/>, <see cref="Side"/>,
///         <see cref="Quantity"/> (order volume) and <see cref="Price"/>.</item>
///   <item><see cref="MessageKind.Delete"/>:
///         <see cref="Symbol"/>, <see cref="OrderId"/>, <see cref="Side"/>.</item>
///   <item><see cref="MessageKind.Execute"/>:
///         <see cref="Symbol"/>, <see cref="OrderId"/>, <see cref="Side"/> and
///         <see cref="Quantity"/> (the traded quantity).</item>
/// </list>
/// <para>
/// The static factory methods make intent explicit at call sites and in tests.
/// </para>
/// </remarks>
public readonly struct FeedMessage
{
    /// <summary>The header sequence number of the message that produced this event.</summary>
    public uint SequenceNumber { get; }

    /// <summary>The event kind.</summary>
    public MessageKind Kind { get; }

    /// <summary>The symbol the order belongs to (trailing pad spaces removed).</summary>
    public string Symbol { get; }

    /// <summary>The order id, unique within a (symbol, side) pair.</summary>
    public ulong OrderId { get; }

    /// <summary>The side the order rests on.</summary>
    public Side Side { get; }

    /// <summary>
    /// For <see cref="MessageKind.Add"/>/<see cref="MessageKind.Update"/> this is the
    /// order's volume; for <see cref="MessageKind.Execute"/> it is the traded quantity.
    /// Unused for <see cref="MessageKind.Delete"/>.
    /// </summary>
    public long Quantity { get; }

    /// <summary>
    /// The order price for <see cref="MessageKind.Add"/>/<see cref="MessageKind.Update"/>.
    /// Unused for the other kinds.
    /// </summary>
    public int Price { get; }

    private FeedMessage(uint sequenceNumber, MessageKind kind, string symbol, ulong orderId, Side side, long quantity, int price)
    {
        SequenceNumber = sequenceNumber;
        Kind = kind;
        Symbol = symbol;
        OrderId = orderId;
        Side = side;
        Quantity = quantity;
        Price = price;
    }

    /// <summary>Creates an Order Added message.</summary>
    public static FeedMessage Add(uint seq, string symbol, ulong orderId, Side side, long volume, int price)
        => new(seq, MessageKind.Add, symbol, orderId, side, volume, price);

    /// <summary>Creates an Order Updated message.</summary>
    public static FeedMessage Update(uint seq, string symbol, ulong orderId, Side side, long volume, int price)
        => new(seq, MessageKind.Update, symbol, orderId, side, volume, price);

    /// <summary>Creates an Order Deleted message.</summary>
    public static FeedMessage Delete(uint seq, string symbol, ulong orderId, Side side)
        => new(seq, MessageKind.Delete, symbol, orderId, side, 0, 0);

    /// <summary>Creates an Order Executed message.</summary>
    public static FeedMessage Execute(uint seq, string symbol, ulong orderId, Side side, long tradedQuantity)
        => new(seq, MessageKind.Execute, symbol, orderId, side, tradedQuantity, 0);

    /// <summary>Creates a placeholder for an unrecognised, skipped message type.</summary>
    public static FeedMessage Unknown(uint seq)
        => new(seq, MessageKind.Unknown, string.Empty, 0, Side.Buy, 0, 0);
}
