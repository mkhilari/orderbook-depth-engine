// -----------------------------------------------------------------------------
// SideBook.cs
//
// One side (all bids, or all asks) of a single symbol's order book.
//
// This is the core data structure of the solution. It keeps two views in lock-step:
// a map of live orders by id, and a price -> total-volume ladder ordered by
// competitiveness. That pairing is what lets add/update/delete/execute run in
// O(log L) and the top levels be read straight off the ladder, already in order.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// One side (bid or ask) of a single symbol's order book.
/// </summary>
/// <remarks>
/// <para>
/// Maintains two structures kept consistent with each other:
/// </para>
/// <list type="bullet">
///   <item>A map of live orders by id, holding each order's current price and volume.
///         This is what add/update/delete/execute operate on.</item>
///   <item>A price-to-aggregate-volume ladder (<see cref="SortedDictionary{TKey,TValue}"/>)
///         that always reflects the sum of order volumes at each price. Iterating it
///         yields levels already ordered by competitiveness — descending price for bids,
///         ascending price for asks — so producing the top levels is a simple take.</item>
/// </list>
/// <para>
/// Only price levels with strictly positive aggregate volume are kept in the ladder,
/// so an order resting at zero volume (the spec permits an order to start at zero and
/// later be updated to non-zero) contributes no visible depth until it gains volume.
/// </para>
/// </remarks>
internal sealed class SideBook
{
    private readonly Dictionary<ulong, OrderEntry> _orders = new();
    private readonly SortedDictionary<int, long> _levels;

    /// <param name="isBid"><see langword="true"/> for the bid side, whose levels are ordered
    /// most-competitive-first by descending price; <see langword="false"/> for the ask side,
    /// ordered ascending.</param>
    public SideBook(bool isBid)
    {
        _levels = isBid
            ? new SortedDictionary<int, long>(DescendingComparer.Instance)
            : new SortedDictionary<int, long>();
    }

    /// <summary>Adds a new order, contributing its volume to its price level.</summary>
    /// <remarks>Order ids are unique per (symbol, side), so an add for a live id should
    /// not occur; should one arrive, the previous contribution is withdrawn first so the
    /// ladder can never be left with an orphaned level (keeping <see cref="Add"/> consistent
    /// with <see cref="Update"/>).</remarks>
    public void Add(ulong orderId, int price, long volume)
    {
        if (_orders.TryGetValue(orderId, out OrderEntry existing))
        {
            RemoveVolume(existing.Price, existing.Volume);
        }
        _orders[orderId] = new OrderEntry(price, volume);
        AddVolume(price, volume);
    }

    /// <summary>Updates an order to a new price and/or volume, moving its volume contribution
    /// from the old price level to the new one.</summary>
    /// <remarks>The body is intentionally identical to <see cref="Add"/>: placing an order at a
    /// (price, volume) is the same operation whether it is new or replacing a prior state, and an
    /// update for an unseen id is treated as a first sighting. Both are kept as separate named
    /// methods so call sites read in the feed's own vocabulary (an 'A' adds, a 'U' updates).</remarks>
    public void Update(ulong orderId, int price, long volume)
    {
        if (_orders.TryGetValue(orderId, out OrderEntry existing))
        {
            RemoveVolume(existing.Price, existing.Volume);
        }
        _orders[orderId] = new OrderEntry(price, volume);
        AddVolume(price, volume);
    }

    /// <summary>Removes an order entirely, withdrawing its remaining volume from the book.</summary>
    public void Delete(ulong orderId)
    {
        if (_orders.Remove(orderId, out OrderEntry existing))
        {
            RemoveVolume(existing.Price, existing.Volume);
        }
    }

    /// <summary>Applies an execution: the traded quantity leaves the book, and an order
    /// that is fully traded is removed (there is no separate delete for it).</summary>
    public void Execute(ulong orderId, long tradedQuantity)
    {
        if (!_orders.TryGetValue(orderId, out OrderEntry existing))
        {
            return;
        }

        // The feed is trusted, but clamp defensively so the ladder can never go negative.
        long traded = Math.Min(tradedQuantity, existing.Volume);
        RemoveVolume(existing.Price, traded);

        long remaining = existing.Volume - traded;
        if (remaining <= 0)
        {
            _orders.Remove(orderId);
        }
        else
        {
            _orders[orderId] = new OrderEntry(existing.Price, remaining);
        }
    }

    /// <summary>
    /// Copies the most competitive <paramref name="maxLevels"/> price levels into
    /// <paramref name="destination"/>, in competitiveness order.
    /// </summary>
    /// <returns>The number of levels written (at most <paramref name="maxLevels"/>).</returns>
    public int CopyTop(int maxLevels, PriceLevel[] destination)
    {
        int count = 0;
        foreach (KeyValuePair<int, long> level in _levels)
        {
            if (count == maxLevels)
            {
                break;
            }
            destination[count++] = new PriceLevel(level.Key, level.Value);
        }
        return count;
    }

    private void AddVolume(int price, long volume)
    {
        if (volume == 0)
        {
            return; // A zero-volume order occupies no visible depth.
        }
        _levels[price] = (_levels.TryGetValue(price, out long current) ? current : 0) + volume;
    }

    private void RemoveVolume(int price, long volume)
    {
        // A zero-volume order never reached the ladder, and a price absent from the ladder had
        // no visible volume to withdraw; either way there is nothing to remove.
        if (volume == 0 || !_levels.TryGetValue(price, out long current))
        {
            return;
        }
        long updated = current - volume;
        if (updated <= 0)
        {
            _levels.Remove(price);
        }
        else
        {
            _levels[price] = updated;
        }
    }

    /// <summary>An order's current state: its price and remaining volume.</summary>
    private readonly record struct OrderEntry(int Price, long Volume);

    /// <summary>Orders integers from highest to lowest, used to keep the bid ladder
    /// most-competitive-first.</summary>
    private sealed class DescendingComparer : IComparer<int>
    {
        public static readonly DescendingComparer Instance = new();
        public int Compare(int x, int y) => y.CompareTo(x);
    }
}
