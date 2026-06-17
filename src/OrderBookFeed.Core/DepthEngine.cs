// -----------------------------------------------------------------------------
// DepthEngine.cs
//
// The public entry point of the core library.
//
// It routes each decoded message to the right symbol's book and returns a snapshot
// only when that message produced a visible change. This is the one type callers
// use, and it guarantees symbols are tracked independently — a change to one never
// emits a snapshot for another.
// -----------------------------------------------------------------------------

namespace OrderBookFeed;

/// <summary>
/// Processes a stream of order book messages and produces a price-depth snapshot of the
/// top levels whenever a message causes a visible change to a symbol's depth.
/// </summary>
/// <remarks>
/// Each symbol is tracked independently: a message that changes one symbol's depth
/// never produces a snapshot for another. A snapshot is produced only when the
/// top levels differ from the one last published for that same symbol.
/// </remarks>
public sealed class DepthEngine
{
    private readonly int _levels;
    private readonly Dictionary<string, SymbolBook> _books = new();

    /// <summary>Creates an engine that publishes the top <paramref name="levels"/> price levels per side.</summary>
    /// <param name="levels">The number of price levels to publish per side. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="levels"/> is less than 1.</exception>
    public DepthEngine(int levels)
    {
        if (levels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(levels), levels, "The number of levels must be at least 1.");
        }
        _levels = levels;
    }

    /// <summary>
    /// Applies a single message and returns the snapshot to publish, or
    /// <see langword="null"/> when the message does not change the visible depth.
    /// </summary>
    public DepthSnapshot? Apply(in FeedMessage message)
    {
        if (message.Kind == MessageKind.Unknown)
        {
            return null;
        }

        if (!_books.TryGetValue(message.Symbol, out SymbolBook? book))
        {
            book = new SymbolBook(message.Symbol, _levels);
            _books[message.Symbol] = book;
        }

        return book.Apply(in message);
    }
}
