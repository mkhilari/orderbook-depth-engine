// -----------------------------------------------------------------------------
// SideBookTests.cs
//
// Unit tests for SideBook, one side of the order book.
//
// They verify volume aggregation, bid/ask ordering, top-levels extraction, and the
// add/update/delete/execute mechanics (including partial/full fills, a re-added id,
// and the zero-volume case) in isolation from the rest of the pipeline.
// -----------------------------------------------------------------------------

using Xunit;

namespace OrderBookFeed.Tests;

public class SideBookTests
{
    private static List<PriceLevel> Top(SideBook book, int levels)
    {
        var buffer = new PriceLevel[levels];
        int count = book.CopyTop(levels, buffer);
        return buffer.Take(count).ToList();
    }

    [Fact]
    public void Aggregates_volume_of_orders_at_the_same_price()
    {
        var book = new SideBook(isBid: true);
        book.Add(orderId: 1, price: 101, volume: 150);
        book.Add(orderId: 2, price: 101, volume: 50);

        Assert.Equal(new[] { new PriceLevel(101, 200) }, Top(book, 5));
    }

    [Fact]
    public void Bids_are_ordered_highest_price_first()
    {
        var book = new SideBook(isBid: true);
        book.Add(1, price: 99, volume: 10);
        book.Add(2, price: 101, volume: 10);
        book.Add(3, price: 100, volume: 10);

        Assert.Equal(new[] { 101, 100, 99 }, Top(book, 5).Select(l => l.Price));
    }

    [Fact]
    public void Asks_are_ordered_lowest_price_first()
    {
        var book = new SideBook(isBid: false);
        book.Add(1, price: 104, volume: 10);
        book.Add(2, price: 102, volume: 10);
        book.Add(3, price: 103, volume: 10);

        Assert.Equal(new[] { 102, 103, 104 }, Top(book, 5).Select(l => l.Price));
    }

    [Fact]
    public void CopyTop_returns_only_the_requested_number_of_levels()
    {
        var book = new SideBook(isBid: true);
        for (int price = 1; price <= 10; price++)
        {
            book.Add((ulong)price, price, volume: 5);
        }

        List<PriceLevel> top3 = Top(book, 3);
        Assert.Equal(new[] { 10, 9, 8 }, top3.Select(l => l.Price));
    }

    [Fact]
    public void Update_moves_volume_to_the_new_price()
    {
        var book = new SideBook(isBid: true);
        book.Add(1, price: 100, volume: 300);
        book.Update(1, price: 105, volume: 300);

        Assert.Equal(new[] { new PriceLevel(105, 300) }, Top(book, 5));
    }

    [Fact]
    public void Update_can_change_volume_at_the_same_price()
    {
        var book = new SideBook(isBid: true);
        book.Add(1, price: 100, volume: 300);
        book.Update(1, price: 100, volume: 50);

        Assert.Equal(new[] { new PriceLevel(100, 50) }, Top(book, 5));
    }

    [Fact]
    public void Delete_withdraws_volume_and_removes_emptied_level()
    {
        var book = new SideBook(isBid: true);
        book.Add(1, price: 100, volume: 300);
        book.Add(2, price: 100, volume: 50);
        book.Delete(1);

        Assert.Equal(new[] { new PriceLevel(100, 50) }, Top(book, 5));

        book.Delete(2);
        Assert.Empty(Top(book, 5));
    }

    [Fact]
    public void Execute_partial_reduces_volume()
    {
        var book = new SideBook(isBid: true);
        book.Add(1, price: 100, volume: 300);
        book.Execute(1, tradedQuantity: 100);

        Assert.Equal(new[] { new PriceLevel(100, 200) }, Top(book, 5));
    }

    [Fact]
    public void Execute_full_removes_the_order()
    {
        var book = new SideBook(isBid: true);
        book.Add(1, price: 100, volume: 300);
        book.Execute(1, tradedQuantity: 300);

        Assert.Empty(Top(book, 5));
    }

    [Fact]
    public void Execute_is_ignored_for_unknown_order()
    {
        var book = new SideBook(isBid: true);
        book.Execute(orderId: 999, tradedQuantity: 10); // No such order; must not throw.

        Assert.Empty(Top(book, 5));
    }

    [Fact]
    public void Re_adding_a_live_order_id_replaces_its_previous_contribution()
    {
        // Order ids are unique per side, so this is malformed input; handle it defensively
        // (consistent with Update) rather than orphaning the old level in the ladder.
        var book = new SideBook(isBid: true);
        book.Add(1, price: 100, volume: 10);
        book.Add(1, price: 200, volume: 5);

        Assert.Equal(new[] { new PriceLevel(200, 5) }, Top(book, 5));
    }

    [Fact]
    public void Zero_volume_order_creates_no_visible_level_until_updated()
    {
        var book = new SideBook(isBid: true);
        book.Add(1, price: 100, volume: 0); // Permitted: an order may start at zero volume.
        Assert.Empty(Top(book, 5));

        book.Update(1, price: 100, volume: 75);
        Assert.Equal(new[] { new PriceLevel(100, 75) }, Top(book, 5));
    }
}
