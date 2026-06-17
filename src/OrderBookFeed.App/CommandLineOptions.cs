// -----------------------------------------------------------------------------
// CommandLineOptions.cs
//
// Parses and validates the command-line arguments: the number of levels and an optional input path.
//
// Isolating argument parsing here keeps Program focused on wiring, and makes the
// parsing rules — and their error messages — straightforward to unit-test on their own.
// -----------------------------------------------------------------------------

using System.Globalization;

namespace OrderBookFeed.App;

/// <summary>
/// Parsed command-line options: the number of <see cref="Levels"/> and an optional
/// input file path. When no path is given the feed is read from standard input.
/// </summary>
internal readonly record struct CommandLineOptions(int Levels, string? InputPath)
{
    /// <summary>Usage text shown on a parsing error.</summary>
    public const string Usage =
        "usage: OrderBookFeed <levels> [inputPath]\n" +
        "  <levels>     number of price-depth levels to publish per side (positive integer)\n" +
        "  [inputPath]  optional path to the binary feed; if omitted, the feed is read from stdin\n" +
        "\n" +
        "examples:\n" +
        "  cat input1.stream | OrderBookFeed 5\n" +
        "  OrderBookFeed 5 input1.stream";

    /// <summary>Attempts to parse the process arguments.</summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <param name="options">The parsed options on success.</param>
    /// <param name="error">A human-readable reason on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeded.</returns>
    public static bool TryParse(string[] args, out CommandLineOptions options, out string? error)
    {
        options = default;
        error = null;

        if (args.Length is < 1 or > 2)
        {
            error = $"expected 1 or 2 arguments, got {args.Length}";
            return false;
        }

        if (!int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int levels) || levels < 1)
        {
            error = $"<levels> must be a positive integer, got '{args[0]}'";
            return false;
        }

        string? inputPath = args.Length == 2 ? args[1] : null;
        options = new CommandLineOptions(levels, inputPath);
        return true;
    }
}
