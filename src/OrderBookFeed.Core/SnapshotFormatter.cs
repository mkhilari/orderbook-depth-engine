// -----------------------------------------------------------------------------
// SnapshotFormatter.cs
//
// Renders a DepthSnapshot to the exact one-line text format the task requires.
//
// It is the single place output text is shaped, which is what makes the output
// byte-for-byte reproducible: the separators, the empty-side "[]", and the
// culture-independent number formatting all live here, isolated from the engine.
// -----------------------------------------------------------------------------

using System.Text;

namespace OrderBookFeed;

/// <summary>
/// Renders a <see cref="DepthSnapshot"/> as the required single-line text format:
/// <code>sequenceNo, symbol, [(bidPrice1, bidVolume1), ...], [(askPrice1, askVolume1), ...]</code>
/// </summary>
/// <remarks>
/// Empty sides render as <c>[]</c>. Prices are printed as their raw integer values.
/// Numbers are written without group separators; the build enables invariant
/// globalization so the output is byte-for-byte identical on every machine.
/// </remarks>
public static class SnapshotFormatter
{
    /// <summary>Formats a snapshot as a single line, without a trailing newline.</summary>
    public static string Format(DepthSnapshot snapshot)
    {
        var builder = new StringBuilder(80);
        Append(builder, snapshot);
        return builder.ToString();
    }

    /// <summary>Appends the formatted snapshot text to <paramref name="builder"/>. No line
    /// terminator is added — the caller writes its own (so output uses LF on every platform).</summary>
    public static void Append(StringBuilder builder, DepthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(snapshot);

        builder.Append(snapshot.SequenceNumber)
               .Append(", ")
               .Append(snapshot.Symbol)
               .Append(", ");
        AppendLevels(builder, snapshot.Bids);
        builder.Append(", ");
        AppendLevels(builder, snapshot.Asks);
    }

    private static void AppendLevels(StringBuilder builder, IReadOnlyList<PriceLevel> levels)
    {
        builder.Append('[');
        for (int i = 0; i < levels.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }
            PriceLevel level = levels[i];
            builder.Append('(')
                   .Append(level.Price)
                   .Append(", ")
                   .Append(level.Volume)
                   .Append(')');
        }
        builder.Append(']');
    }
}
