// -----------------------------------------------------------------------------
// TestFeed.cs
//
// Test helper that builds binary feed messages — the encoder counterpart to FeedReader.
//
// It lets tests construct exact wire-format byte streams (Add/Update/Delete/Execute,
// or an arbitrary raw message) to drive the decoder and engine, so the parsing and
// book logic can be exercised directly without depending on the provided sample files.
// -----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Text;

namespace OrderBookFeed.Tests;

/// <summary>
/// Builds binary feed messages for tests. This is the encoder counterpart to
/// <see cref="FeedReader"/>; encoding here and decoding there exercises the exact
/// wire layout described in the task specification.
/// </summary>
internal static class TestFeed
{
    public static byte[] Add(uint seq, string symbol, ulong orderId, char side, ulong size, int price)
        => WithHeader(seq, Body('A', symbol, orderId, side, b =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16, 8), size);
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(24, 4), price);
        }, bodyLength: 32));

    public static byte[] Update(uint seq, string symbol, ulong orderId, char side, ulong size, int price)
        => WithHeader(seq, Body('U', symbol, orderId, side, b =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16, 8), size);
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(24, 4), price);
        }, bodyLength: 32));

    public static byte[] Delete(uint seq, string symbol, ulong orderId, char side)
        => WithHeader(seq, Body('D', symbol, orderId, side, _ => { }, bodyLength: 16));

    public static byte[] Execute(uint seq, string symbol, ulong orderId, char side, ulong tradedQuantity)
        => WithHeader(seq, Body('E', symbol, orderId, side, b =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16, 8), tradedQuantity);
        }, bodyLength: 24));

    /// <summary>Builds a message with an arbitrary (e.g. unrecognised) type byte and body length.</summary>
    public static byte[] Raw(uint seq, char type, int bodyLength)
    {
        var body = new byte[bodyLength];
        if (bodyLength > 0)
        {
            body[0] = (byte)type;
        }
        return WithHeader(seq, body);
    }

    public static MemoryStream Stream(params byte[][] messages)
    {
        var stream = new MemoryStream();
        foreach (byte[] message in messages)
        {
            stream.Write(message, 0, message.Length);
        }
        stream.Position = 0;
        return stream;
    }

    private static byte[] Body(char type, string symbol, ulong orderId, char side, Action<byte[]> fillRest, int bodyLength)
    {
        var body = new byte[bodyLength];
        Array.Fill(body, (byte)' '); // Reserved/padding fields are spaces.
        body[0] = (byte)type;
        WriteAlpha(body.AsSpan(1, 3), symbol);
        BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(4, 8), orderId);
        body[12] = (byte)side;
        fillRest(body);
        return body;
    }

    private static void WriteAlpha(Span<byte> destination, string value)
    {
        destination.Fill((byte)' ');
        Encoding.ASCII.GetBytes(value).CopyTo(destination); // Left-justified.
    }

    private static byte[] WithHeader(uint seq, byte[] body)
    {
        var message = new byte[FeedReader.HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(0, 4), seq);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4, 4), (uint)body.Length);
        body.CopyTo(message, FeedReader.HeaderSize);
        return message;
    }
}
