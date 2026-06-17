// -----------------------------------------------------------------------------
// Program.cs
//
// The console application: the process entry point and all I/O wiring.
//
// It connects the input (a file argument or stdin) to the DepthEngine and writes
// each published snapshot to stdout as an LF-terminated line. Keeping this host thin
// — and free of any book logic — is what lets the domain library stay I/O-free and
// fully unit-testable.
// -----------------------------------------------------------------------------

using System.Text;

namespace OrderBookFeed.App;

/// <summary>
/// Command-line entry point. Reads a binary order book feed (from a file argument or
/// standard input), processes it with <see cref="DepthEngine"/>, and writes each
/// changed depth snapshot to standard output, one per line.
/// </summary>
internal static class Program
{
    private const int IoBufferSize = 1 << 16; // 64 KiB

    private static int Main(string[] args)
    {
        if (!CommandLineOptions.TryParse(args, out CommandLineOptions options, out string? error))
        {
            Console.Error.WriteLine($"error: {error}");
            Console.Error.WriteLine(CommandLineOptions.Usage);
            return ExitCode.UsageError;
        }

        try
        {
            using Stream rawInput = options.InputPath is null
                ? Console.OpenStandardInput()
                : File.OpenRead(options.InputPath);
            using var input = new BufferedStream(rawInput, IoBufferSize);

            // Write LF-terminated lines with no BOM, independent of the host platform,
            // so the output matches the reference format byte-for-byte.
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), IoBufferSize)
            {
                AutoFlush = false,
            };

            Run(input, output, options.Levels);
            output.Flush();
            return ExitCode.Success;
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"error: input file not found: {options.InputPath}");
            return ExitCode.UsageError;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"error: input path not found: {options.InputPath}");
            return ExitCode.UsageError;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
        {
            // The spec guarantees well-formed input; if the feed is nonetheless truncated
            // or malformed, fail with a concise message instead of an unhandled stack trace.
            Console.Error.WriteLine($"error: malformed feed: {ex.Message}");
            return ExitCode.DataError;
        }
    }

    /// <summary>
    /// Core pipeline: decode messages from <paramref name="input"/>, feed them through a
    /// fresh <see cref="DepthEngine"/>, and write every published snapshot to
    /// <paramref name="output"/> as an LF-terminated line. Exposed to the test project.
    /// </summary>
    internal static void Run(Stream input, TextWriter output, int levels)
    {
        var engine = new DepthEngine(levels);
        var reader = new FeedReader(input);
        var line = new StringBuilder(80);

        foreach (FeedMessage message in reader.ReadMessages())
        {
            DepthSnapshot? snapshot = engine.Apply(in message);
            if (snapshot is null)
            {
                continue;
            }

            line.Clear();
            SnapshotFormatter.Append(line, snapshot);
            line.Append('\n');
            output.Write(line);
        }
    }

    private static class ExitCode
    {
        public const int Success = 0;
        public const int DataError = 1;
        public const int UsageError = 2;
    }
}
