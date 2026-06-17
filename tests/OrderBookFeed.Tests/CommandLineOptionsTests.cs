// -----------------------------------------------------------------------------
// CommandLineOptionsTests.cs
//
// Unit tests for CommandLineOptions, the CLI argument parser.
//
// They cover the accepted forms (levels only, levels + path) and every rejection
// case (missing, non-numeric, non-positive, or too many arguments).
// -----------------------------------------------------------------------------

using OrderBookFeed.App;
using Xunit;

namespace OrderBookFeed.Tests;

public class CommandLineOptionsTests
{
    [Fact]
    public void Parses_levels_only_and_defaults_to_stdin()
    {
        Assert.True(CommandLineOptions.TryParse(["5"], out CommandLineOptions options, out _));
        Assert.Equal(5, options.Levels);
        Assert.Null(options.InputPath);
    }

    [Fact]
    public void Parses_levels_and_input_path()
    {
        Assert.True(CommandLineOptions.TryParse(["10", "data.stream"], out CommandLineOptions options, out _));
        Assert.Equal(10, options.Levels);
        Assert.Equal("data.stream", options.InputPath);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("3.5")]
    [InlineData("")]
    public void Rejects_invalid_levels(string levels)
    {
        Assert.False(CommandLineOptions.TryParse([levels], out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_missing_arguments()
    {
        Assert.False(CommandLineOptions.TryParse([], out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_too_many_arguments()
    {
        Assert.False(CommandLineOptions.TryParse(["5", "a", "b"], out _, out string? error));
        Assert.NotNull(error);
    }
}
