// -----------------------------------------------------------------------------
// FeedReaderTests.cs
//
// Unit tests for FeedReader, the binary decoder.
//
// They pin the wire-format contract: each message type's field layout, binary
// decoding, signed/negative prices, symbol padding, and the edge cases (unknown,
// zero-length, and truncated messages).
// -----------------------------------------------------------------------------

using Xunit;

namespace OrderBookFeed.Tests;

public class FeedReaderTests
{
    private static List<FeedMessage> ReadAll(params byte[][] messages)
        => new FeedReader(TestFeed.Stream(messages)).ReadMessages().ToList();

    [Fact]
    public void Decodes_Add_message_fields()
    {
        FeedMessage m = Assert.Single(ReadAll(
            TestFeed.Add(seq: 7, symbol: "VC0", orderId: 0x6101_8C41_0002_4248, side: 'B', size: 5000, price: 318800)));

        Assert.Equal(7u, m.SequenceNumber);
        Assert.Equal(MessageKind.Add, m.Kind);
        Assert.Equal("VC0", m.Symbol);
        Assert.Equal(0x6101_8C41_0002_4248ul, m.OrderId);
        Assert.Equal(Side.Buy, m.Side);
        Assert.Equal(5000L, m.Quantity);
        Assert.Equal(318800, m.Price);
    }

    [Fact]
    public void Decodes_Update_message_fields()
    {
        FeedMessage m = Assert.Single(ReadAll(
            TestFeed.Update(seq: 3, symbol: "VC9", orderId: 42, side: 'S', size: 250, price: 1000)));

        Assert.Equal(MessageKind.Update, m.Kind);
        Assert.Equal("VC9", m.Symbol);
        Assert.Equal(42ul, m.OrderId);
        Assert.Equal(Side.Sell, m.Side);
        Assert.Equal(250L, m.Quantity);
        Assert.Equal(1000, m.Price);
    }

    [Fact]
    public void Decodes_Delete_message_fields()
    {
        FeedMessage m = Assert.Single(ReadAll(
            TestFeed.Delete(seq: 11, symbol: "VC1", orderId: 99, side: 'B')));

        Assert.Equal(MessageKind.Delete, m.Kind);
        Assert.Equal("VC1", m.Symbol);
        Assert.Equal(99ul, m.OrderId);
        Assert.Equal(Side.Buy, m.Side);
    }

    [Fact]
    public void Decodes_Execute_message_fields()
    {
        FeedMessage m = Assert.Single(ReadAll(
            TestFeed.Execute(seq: 5, symbol: "VC2", orderId: 8, side: 'S', tradedQuantity: 201)));

        Assert.Equal(MessageKind.Execute, m.Kind);
        Assert.Equal("VC2", m.Symbol);
        Assert.Equal(8ul, m.OrderId);
        Assert.Equal(Side.Sell, m.Side);
        Assert.Equal(201L, m.Quantity); // Quantity carries the traded amount for executions.
    }

    [Fact]
    public void Decodes_negative_price()
    {
        FeedMessage m = Assert.Single(ReadAll(
            TestFeed.Add(seq: 1, symbol: "VC0", orderId: 1, side: 'B', size: 10, price: -12345)));

        Assert.Equal(-12345, m.Price);
    }

    [Fact]
    public void Trims_symbol_padding()
    {
        // A two-character symbol is left-justified and right-padded with a space.
        FeedMessage m = Assert.Single(ReadAll(
            TestFeed.Add(seq: 1, symbol: "AB", orderId: 1, side: 'B', size: 10, price: 100)));

        Assert.Equal("AB", m.Symbol);
    }

    [Fact]
    public void Reads_multiple_messages_in_order()
    {
        List<FeedMessage> messages = ReadAll(
            TestFeed.Add(1, "VC0", 1, 'B', 100, 500),
            TestFeed.Execute(2, "VC0", 1, 'B', 40),
            TestFeed.Delete(3, "VC0", 1, 'B'));

        Assert.Equal(3, messages.Count);
        Assert.Equal(new[] { 1u, 2u, 3u }, messages.Select(m => m.SequenceNumber));
        Assert.Equal(new[] { MessageKind.Add, MessageKind.Execute, MessageKind.Delete }, messages.Select(m => m.Kind));
    }

    [Fact]
    public void Skips_unknown_message_types_using_declared_length()
    {
        // An unrecognised 'Z' message between two known ones must be skipped cleanly,
        // and the following known message must still decode.
        List<FeedMessage> messages = ReadAll(
            TestFeed.Add(1, "VC0", 1, 'B', 100, 500),
            TestFeed.Raw(seq: 2, type: 'Z', bodyLength: 40),
            TestFeed.Add(3, "VC0", 2, 'B', 100, 500));

        Assert.Equal(3, messages.Count);
        Assert.Equal(MessageKind.Add, messages[0].Kind);
        Assert.Equal(MessageKind.Unknown, messages[1].Kind);
        Assert.Equal(MessageKind.Add, messages[2].Kind);
        Assert.Equal(3u, messages[2].SequenceNumber);
    }

    [Fact]
    public void Skips_zero_length_body_as_unknown()
    {
        // A header declaring a zero-byte body carries no type byte; it must be skipped
        // cleanly (as Unknown), not crash, and the following message must still decode.
        List<FeedMessage> messages = ReadAll(
            TestFeed.Raw(seq: 1, type: '?', bodyLength: 0),
            TestFeed.Add(2, "VC0", 1, 'B', 100, 500));

        Assert.Equal(2, messages.Count);
        Assert.Equal(MessageKind.Unknown, messages[0].Kind);
        Assert.Equal(MessageKind.Add, messages[1].Kind);
    }

    [Fact]
    public void Empty_stream_yields_no_messages()
    {
        Assert.Empty(ReadAll());
    }

    [Fact]
    public void Throws_on_truncated_body()
    {
        byte[] full = TestFeed.Add(1, "VC0", 1, 'B', 100, 500);
        byte[] truncated = full[..(full.Length - 4)]; // Drop the trailing reserved bytes.

        var reader = new FeedReader(TestFeed.Stream(truncated));
        Assert.Throws<EndOfStreamException>(() => reader.ReadMessages().ToList());
    }
}
