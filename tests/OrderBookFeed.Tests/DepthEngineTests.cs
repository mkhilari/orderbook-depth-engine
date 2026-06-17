// -----------------------------------------------------------------------------
// DepthEngineTests.cs
//
// Unit tests for DepthEngine, the end-to-end change-detection behavior.
//
// They pin the rules that matter most: publish only on a visible change to the top levels,
// track each symbol independently, carry the sequence number of the causing message,
// and reproduce the worked example from the task's "Order Book & Price Depth" section.
// -----------------------------------------------------------------------------

using Xunit;

namespace OrderBookFeed.Tests;

public class DepthEngineTests
{
    /// <summary>Runs messages through a fresh engine and returns the published snapshots, formatted.</summary>
    private static List<string> Run(int levels, params byte[][] messages)
    {
        var engine = new DepthEngine(levels);
        var reader = new FeedReader(TestFeed.Stream(messages));
        var published = new List<string>();
        foreach (FeedMessage message in reader.ReadMessages())
        {
            DepthSnapshot? snapshot = engine.Apply(in message);
            if (snapshot is not null)
            {
                published.Add(SnapshotFormatter.Format(snapshot));
            }
        }
        return published;
    }

    [Fact]
    public void Constructor_rejects_non_positive_levels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DepthEngine(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DepthEngine(-1));
    }

    [Fact]
    public void Publishes_on_first_visible_change()
    {
        List<string> output = Run(5, TestFeed.Add(1, "VC0", 1, 'B', 5000, 318800));
        Assert.Equal(new[] { "1, VC0, [(318800, 5000)], []" }, output);
    }

    [Fact]
    public void Does_not_publish_when_top_levels_unchanged()
    {
        // Re-stating an order with identical price and volume changes nothing visible.
        List<string> output = Run(5,
            TestFeed.Add(1, "VC0", 1, 'B', 100, 500),
            TestFeed.Update(2, "VC0", 1, 'B', 100, 500));

        Assert.Equal(new[] { "1, VC0, [(500, 100)], []" }, output);
    }

    [Fact]
    public void Changes_beyond_the_depth_window_do_not_publish()
    {
        // With 2 levels, only the two most competitive bid levels are visible.
        List<string> output = Run(2,
            TestFeed.Add(1, "VC0", 1, 'B', 10, 100), // visible
            TestFeed.Add(2, "VC0", 2, 'B', 10, 99),  // visible
            TestFeed.Add(3, "VC0", 3, 'B', 10, 98),  // 3rd level — not visible, no publish
            TestFeed.Execute(4, "VC0", 3, 'B', 5),   // touches the hidden level — no publish
            TestFeed.Delete(5, "VC0", 1, 'B'));       // removes top level — 98 becomes visible, publish

        Assert.Equal(new[]
        {
            "1, VC0, [(100, 10)], []",
            "2, VC0, [(100, 10), (99, 10)], []",
            "5, VC0, [(99, 10), (98, 5)], []",
        }, output);
    }

    [Fact]
    public void Tracks_each_symbol_independently()
    {
        List<string> output = Run(5,
            TestFeed.Add(1, "VC0", 1, 'B', 10, 100),
            TestFeed.Add(2, "VC1", 1, 'B', 20, 200),
            TestFeed.Add(3, "VC0", 2, 'S', 30, 110));

        Assert.Equal(new[]
        {
            "1, VC0, [(100, 10)], []",
            "2, VC1, [(200, 20)], []",
            "3, VC0, [(100, 10)], [(110, 30)]",
        }, output);
    }

    [Fact]
    public void Allows_a_crossed_book()
    {
        // The spec notes bid and ask may cross; no special handling, both are shown.
        List<string> output = Run(5,
            TestFeed.Add(1, "VC0", 1, 'B', 10, 105),
            TestFeed.Add(2, "VC0", 2, 'S', 10, 100));

        Assert.Equal("2, VC0, [(105, 10)], [(100, 10)]", output[^1]);
    }

    [Fact]
    public void Unknown_messages_do_not_publish()
    {
        List<string> output = Run(5,
            TestFeed.Add(1, "VC0", 1, 'B', 10, 100),
            TestFeed.Raw(2, 'Z', bodyLength: 8));

        Assert.Single(output);
    }

    [Fact]
    public void Volume_only_change_at_a_visible_level_publishes()
    {
        List<string> output = Run(5,
            TestFeed.Add(1, "VC0", 1, 'B', 10, 100),
            TestFeed.Update(2, "VC0", 1, 'B', 20, 100)); // same price, new volume — visible change

        Assert.Equal(new[]
        {
            "1, VC0, [(100, 10)], []",
            "2, VC0, [(100, 20)], []",
        }, output);
    }

    [Fact]
    public void Order_can_be_re_added_after_executing_to_zero()
    {
        List<string> output = Run(5,
            TestFeed.Add(1, "VC0", 1, 'B', 10, 100),
            TestFeed.Execute(2, "VC0", 1, 'B', 10), // fully traded -> leaves the book
            TestFeed.Add(3, "VC0", 1, 'B', 7, 100)); // same id added again

        Assert.Equal(new[]
        {
            "1, VC0, [(100, 10)], []",
            "2, VC0, [], []",
            "3, VC0, [(100, 7)], []",
        }, output);
    }

    [Fact]
    public void Update_to_zero_empties_a_visible_level()
    {
        List<string> output = Run(5,
            TestFeed.Add(1, "VC0", 1, 'B', 10, 100),
            TestFeed.Update(2, "VC0", 1, 'B', 0, 100)); // volume drops to zero -> level disappears

        Assert.Equal(new[]
        {
            "1, VC0, [(100, 10)], []",
            "2, VC0, [], []",
        }, output);
    }

    [Fact]
    public void Update_for_an_unknown_order_acts_as_an_implicit_add()
    {
        List<string> output = Run(5, TestFeed.Update(1, "VC0", 1, 'B', 30, 700));
        Assert.Equal(new[] { "1, VC0, [(700, 30)], []" }, output);
    }

    [Fact]
    public void Reproduces_the_specification_depth_example()
    {
        // The order book from the "Order Book & Price Depth" section, shown at 3 levels.
        List<string> output = Run(3,
            TestFeed.Add(1, "VC1", 10200, 'B', 150, 101),
            TestFeed.Add(2, "VC1", 10444, 'B', 50, 101),
            TestFeed.Add(3, "VC1", 10232, 'B', 300, 100),
            TestFeed.Add(4, "VC1", 10333, 'B', 50, 99),
            TestFeed.Add(5, "VC1", 10215, 'B', 150, 99),
            TestFeed.Add(6, "VC1", 10010, 'B', 100, 98),
            TestFeed.Add(7, "VC1", 10108, 'S', 5, 102),
            TestFeed.Add(8, "VC1", 10040, 'S', 10, 103),
            TestFeed.Add(9, "VC1", 10088, 'S', 10, 103),
            TestFeed.Add(10, "VC1", 10101, 'S', 100, 104));

        Assert.Equal(
            "10, VC1, [(101, 200), (100, 300), (99, 200)], [(102, 5), (103, 20), (104, 100)]",
            output[^1]);
    }
}
