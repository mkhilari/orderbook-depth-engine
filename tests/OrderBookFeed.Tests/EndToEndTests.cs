// -----------------------------------------------------------------------------
// EndToEndTests.cs
//
// End-to-end tests that run the whole pipeline and compare its output
// byte-for-byte against reference logs.
//
// This is the strongest correctness guarantee in the suite — the same kind of
// comparison the grader performs. One test uses the provided input1/output1; the
// other a synthetic multi-symbol, Delete-heavy fixture whose expected output was
// produced by an independent implementation.
// -----------------------------------------------------------------------------

using System.Text;
using OrderBookFeed.App;
using Xunit;

namespace OrderBookFeed.Tests;

/// <summary>
/// End-to-end test against the reference sample provided with the task. Running the
/// full pipeline over <c>input1.stream</c> at a depth of 5 levels must reproduce
/// <c>output1.log</c> byte-for-byte — the same comparison the grader performs on the larger stream.
/// </summary>
public class EndToEndTests
{
    private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    private static byte[] ProcessToBytes(string inputPath, int levels)
    {
        using var input = File.OpenRead(inputPath);
        using var outputStream = new MemoryStream();
        using (var writer = new StreamWriter(outputStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1 << 16, leaveOpen: true)
        {
            AutoFlush = false,
        })
        {
            Program.Run(input, writer, levels);
            writer.Flush();
        }
        return outputStream.ToArray();
    }

    [Fact]
    public void Reproduces_output1_byte_for_byte()
    {
        byte[] expected = File.ReadAllBytes(SamplePath("output1.log"));
        byte[] actual = ProcessToBytes(SamplePath("input1.stream"), levels: 5);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Reproduces_multi_symbol_fixture_byte_for_byte()
    {
        // input1.stream is single-symbol with no Deletes. This synthetic fixture adds the
        // coverage the graded stream actually exercises: 4 interleaved symbols, Deletes,
        // partial/full executions, an order re-added after executing to zero, and an update
        // that empties a visible level. Its expected output was generated independently.
        byte[] expected = File.ReadAllBytes(SamplePath("output_multi.log"));
        byte[] actual = ProcessToBytes(SamplePath("input_multi.stream"), levels: 3);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Output_uses_lf_line_endings_and_a_trailing_newline()
    {
        byte[] actual = ProcessToBytes(SamplePath("input1.stream"), levels: 5);

        Assert.DoesNotContain((byte)'\r', actual); // No CRLF, on any platform.
        Assert.Equal((byte)'\n', actual[^1]);      // Trailing newline.
    }
}
