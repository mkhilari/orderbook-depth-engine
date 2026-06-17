// -----------------------------------------------------------------------------
// MessageKind.cs
//
// Defines `MessageKind` — the four order-book event types (Add, Update, Delete,
// Execute), plus Unknown for message types the reader skips.
//
// The decoder tags every message with one of these and the engine switches on it.
// Keeping it a named enum means the rest of the code reasons about intent instead
// of raw message-type bytes.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// The kind of order book event carried by a feed message, identified by the
/// leading message-type byte of each message body.
/// </summary>
public enum MessageKind : byte
{
    /// <summary>An unrecognised message type. Such messages are skipped using the
    /// length declared in the header, which keeps the reader forward compatible
    /// with message types that are not relevant to depth construction.</summary>
    Unknown = 0,

    /// <summary>Order Added — <c>'A'</c>. A new order joins the book.</summary>
    Add,

    /// <summary>Order Updated — <c>'U'</c>. An existing order changes price and/or volume.</summary>
    Update,

    /// <summary>Order Deleted — <c>'D'</c>. An existing order is removed from the book.</summary>
    Delete,

    /// <summary>Order Executed — <c>'E'</c>. An order trades part or all of its volume;
    /// the traded amount is removed and a fully traded order leaves the book.</summary>
    Execute,
}
