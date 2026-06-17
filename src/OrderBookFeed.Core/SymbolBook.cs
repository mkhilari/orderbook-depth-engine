// -----------------------------------------------------------------------------
// SymbolBook.cs
//
// The full order book for one symbol: its bid SideBook, its ask SideBook, and the
// bookkeeping that decides when to publish.
//
// It owns the rule at the heart of the task — emit a snapshot only when the visible
// top levels actually changed for this symbol — and sits between the engine's
// per-symbol routing and the two sides.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// The full order book for a single symbol: both sides plus the bookkeeping
/// needed to detect a visible change to the top levels.
/// </summary>
internal sealed class SymbolBook
{
    private readonly string _symbol;
    private readonly int _levels;

    private readonly SideBook _bids = new(isBid: true);
    private readonly SideBook _asks = new(isBid: false);

    // Reusable scratch buffers so an unchanged depth costs no allocations.
    private readonly PriceLevel[] _bidScratch;
    private readonly PriceLevel[] _askScratch;

    // The depth most recently published for this symbol. The baseline is an empty
    // book, so the first message that creates visible depth is itself a change.
    private PriceLevel[] _lastBids = Array.Empty<PriceLevel>();
    private PriceLevel[] _lastAsks = Array.Empty<PriceLevel>();

    public SymbolBook(string symbol, int levels)
    {
        _symbol = symbol;
        _levels = levels;
        _bidScratch = new PriceLevel[levels];
        _askScratch = new PriceLevel[levels];
    }

    /// <summary>
    /// Applies a message to this book and returns a snapshot if, and only if, the
    /// top levels changed as a result; otherwise returns <see langword="null"/>.
    /// </summary>
    public DepthSnapshot? Apply(in FeedMessage message)
    {
        SideBook side = message.Side == Side.Buy ? _bids : _asks;
        switch (message.Kind)
        {
            case MessageKind.Add:
                side.Add(message.OrderId, message.Price, message.Quantity);
                break;
            case MessageKind.Update:
                side.Update(message.OrderId, message.Price, message.Quantity);
                break;
            case MessageKind.Delete:
                side.Delete(message.OrderId);
                break;
            case MessageKind.Execute:
                side.Execute(message.OrderId, message.Quantity);
                break;
            default:
                return null; // Unknown kinds never reach here, but ignore them defensively.
        }

        int bidCount = _bids.CopyTop(_levels, _bidScratch);
        int askCount = _asks.CopyTop(_levels, _askScratch);

        if (Matches(_bidScratch, bidCount, _lastBids) && Matches(_askScratch, askCount, _lastAsks))
        {
            return null; // No visible change to the top levels.
        }

        _lastBids = _bidScratch[..bidCount]; // Trimmed, owned copies for storage and publishing.
        _lastAsks = _askScratch[..askCount];
        return new DepthSnapshot(message.SequenceNumber, _symbol, _lastBids, _lastAsks);
    }

    private static bool Matches(PriceLevel[] candidate, int count, PriceLevel[] previous)
    {
        if (count != previous.Length)
        {
            return false;
        }
        for (int i = 0; i < count; i++)
        {
            if (!candidate[i].Equals(previous[i]))
            {
                return false;
            }
        }
        return true;
    }
}
