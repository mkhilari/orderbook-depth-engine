// -----------------------------------------------------------------------------
// SnapshotFormatterTests.cs
//
// Unit tests for SnapshotFormatter, the output line rendering.
//
// They lock the exact text format down to the character: separators, empty sides
// rendered as "[]", multiple levels joined correctly, and negative prices without
// grouping separators.
// -----------------------------------------------------------------------------

using Xunit;

namespace OrderBookFeed.Tests;

public class SnapshotFormatterTests
{
    private static DepthSnapshot Snapshot(uint seq, string symbol, PriceLevel[] bids, PriceLevel[] asks)
        => new(seq, symbol, bids, asks);

    [Fact]
    public void Formats_empty_sides_as_empty_brackets()
    {
        string line = SnapshotFormatter.Format(Snapshot(5, "VC0", [], []));
        Assert.Equal("5, VC0, [], []", line);
    }

    [Fact]
    public void Formats_a_single_level_on_each_side()
    {
        string line = SnapshotFormatter.Format(
            Snapshot(4, "VC0", [new PriceLevel(318800, 4709)], [new PriceLevel(318900, 360)]));
        Assert.Equal("4, VC0, [(318800, 4709)], [(318900, 360)]", line);
    }

    [Fact]
    public void Joins_multiple_levels_with_comma_space()
    {
        string line = SnapshotFormatter.Format(Snapshot(7, "VC0",
            [new PriceLevel(319000, 888), new PriceLevel(318800, 4709)],
            []));
        Assert.Equal("7, VC0, [(319000, 888), (318800, 4709)], []", line);
    }

    [Fact]
    public void Formats_negative_prices_without_grouping_separators()
    {
        string line = SnapshotFormatter.Format(Snapshot(1, "VC0",
            [new PriceLevel(-12345, 1000000)],
            []));
        Assert.Equal("1, VC0, [(-12345, 1000000)], []", line);
    }
}
