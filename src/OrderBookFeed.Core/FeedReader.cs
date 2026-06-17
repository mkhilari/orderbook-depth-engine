// -----------------------------------------------------------------------------
// FeedReader.cs
//
// Decodes the binary order-book feed into strongly-typed FeedMessage values.
//
// This is the first pipeline stage and the single place that knows the wire format
// (header framing, field offsets, the binary field layout). Concentrating all byte-level
// parsing here lets the rest of the codebase work with clean domain objects, and the
// header's declared length lets it skip unknown message types so it stays forward
// compatible.
// -----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Text;

namespace OrderBookFeed;

/// <summary>
/// Decodes a binary order book feed stream into a sequence of
/// <see cref="FeedMessage"/> values.
/// </summary>
/// <remarks>
/// <para>
/// Every message is framed by an 8-byte header:
/// </para>
/// <list type="bullet">
///   <item>Sequence number — <c>uint32</c>.</item>
///   <item>Message size — <c>uint32</c>, the body length in bytes, excluding the header.</item>
/// </list>
/// <para>
/// The body begins with a one-byte message type. Because the header declares the
/// body length, message types this reader does not recognise are skipped cleanly,
/// which keeps it forward compatible. Fields are decoded with explicit
/// <see cref="BinaryPrimitives"/> helpers, so the result is identical on every machine.
/// </para>
/// <para>
/// The reader is pull-based and streams the input: it never holds more than a
/// single message in memory, so it handles arbitrarily large feeds.
/// </para>
/// </remarks>
public sealed class FeedReader
{
    /// <summary>Size of the per-message header in bytes (sequence number + message size).</summary>
    public const int HeaderSize = 8;

    // Field offsets within a message body. The body starts at the message-type byte.
    private const int SymbolOffset = 1;   // Alpha(3)
    private const int SymbolLength = 3;
    private const int OrderIdOffset = 4;  // Numeric(8)
    private const int SideOffset = 12;    // Alpha(1)
    // Reserved Alpha(3) at offset 13.
    private const int QuantityOffset = 16; // Numeric(8) — order size (A/U) or traded qty (E)
    private const int PriceOffset = 24;    // Price(4, signed) — A/U only

    private readonly Stream _stream;
    private byte[] _bodyBuffer = new byte[64];

    /// <summary>Creates a reader over the supplied stream.</summary>
    /// <param name="stream">The binary feed. The reader does not take ownership and will not dispose it.</param>
    public FeedReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Reads and decodes every message from the stream, lazily.
    /// </summary>
    /// <returns>A sequence of decoded messages, including <see cref="MessageKind.Unknown"/>
    /// placeholders for skipped message types.</returns>
    public IEnumerable<FeedMessage> ReadMessages()
    {
        while (TryReadMessage(out FeedMessage message))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Reads and decodes the next message.
    /// </summary>
    /// <param name="message">The decoded message when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if a message was read; <see langword="false"/> at end of stream.</returns>
    public bool TryReadMessage(out FeedMessage message)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        if (!TryReadExact(header))
        {
            message = default;
            return false; // Clean end of stream on a message boundary.
        }

        uint sequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(header);
        uint bodySize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);

        if (_bodyBuffer.Length < bodySize)
        {
            _bodyBuffer = new byte[bodySize];
        }

        Span<byte> body = _bodyBuffer.AsSpan(0, (int)bodySize);
        if (!TryReadExact(body))
        {
            throw new EndOfStreamException(
                $"Stream truncated: message {sequenceNumber} declared a {bodySize}-byte body that is not fully present.");
        }

        message = Decode(sequenceNumber, body);
        return true;
    }

    private static FeedMessage Decode(uint sequenceNumber, ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return FeedMessage.Unknown(sequenceNumber); // A zero-length body carries no type; skip it.
        }

        char type = (char)body[0];
        return type switch
        {
            'A' => FeedMessage.Add(sequenceNumber, ReadSymbol(body), ReadOrderId(body), ReadSide(body), ReadQuantity(body), ReadPrice(body)),
            'U' => FeedMessage.Update(sequenceNumber, ReadSymbol(body), ReadOrderId(body), ReadSide(body), ReadQuantity(body), ReadPrice(body)),
            'D' => FeedMessage.Delete(sequenceNumber, ReadSymbol(body), ReadOrderId(body), ReadSide(body)),
            'E' => FeedMessage.Execute(sequenceNumber, ReadSymbol(body), ReadOrderId(body), ReadSide(body), ReadQuantity(body)),
            _ => FeedMessage.Unknown(sequenceNumber),
        };
    }

    private static string ReadSymbol(ReadOnlySpan<byte> body)
        => Encoding.ASCII.GetString(body.Slice(SymbolOffset, SymbolLength)).TrimEnd(' ');

    private static ulong ReadOrderId(ReadOnlySpan<byte> body)
        => BinaryPrimitives.ReadUInt64LittleEndian(body[OrderIdOffset..]);

    private static Side ReadSide(ReadOnlySpan<byte> body) => (char)body[SideOffset] switch
    {
        'B' => Side.Buy,
        'S' => Side.Sell,
        var other => throw new InvalidDataException($"Unexpected order side '{other}'."),
    };

    private static long ReadQuantity(ReadOnlySpan<byte> body)
    {
        ulong quantity = BinaryPrimitives.ReadUInt64LittleEndian(body[QuantityOffset..]);
        // Volumes are modelled as signed 64-bit so that decrements can be checked
        // against zero. Real volumes are far below this range; guard the assumption.
        if (quantity > long.MaxValue)
        {
            throw new InvalidDataException($"Quantity {quantity} exceeds the supported range.");
        }
        return (long)quantity;
    }

    private static int ReadPrice(ReadOnlySpan<byte> body)
        => BinaryPrimitives.ReadInt32LittleEndian(body[PriceOffset..]); // Signed: prices may be negative.

    /// <summary>Fills <paramref name="destination"/> completely, tolerating short reads.
    /// Returns <see langword="false"/> only when the stream ends before any byte is read.</summary>
    private bool TryReadExact(Span<byte> destination)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int n = _stream.Read(destination[read..]);
            if (n == 0)
            {
                if (read == 0)
                {
                    return false; // End of stream exactly on a boundary.
                }
                throw new EndOfStreamException("Stream ended in the middle of a message.");
            }
            read += n;
        }
        return true;
    }
}
