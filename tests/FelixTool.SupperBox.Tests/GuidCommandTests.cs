using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

public sealed class GuidCommandTests
{
    private sealed class CapturingOutput : IOutputService
    {
        public string? Content { get; private set; }

        public Task ShowAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            return Task.CompletedTask;
        }
    }

    private static async Task<string> RunAsync(string input)
    {
        var output = new CapturingOutput();
        var registry = new CommandRegistry([new GuidCommand(output)]);
        await registry.ExecuteAsync(input, CancellationToken.None);
        return output.Content!;
    }

    [Fact]
    public async Task Random_GeneratesValidGuid()
    {
        Assert.True(Guid.TryParse(await RunAsync("guid random"), out _));
    }

    [Theory]
    [InlineData("guid seq 1", "00000000-0000-0000-0000-000000000001")]
    [InlineData("guid seq 10", "00000000-0000-0000-0000-00000000000A")]
    [InlineData("guid seq 255", "00000000-0000-0000-0000-0000000000FF")]
    [InlineData("guid repeat 0", "00000000-0000-0000-0000-000000000000")]
    [InlineData("guid repeat F", "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
    public async Task Strategies_GenerateExpectedGuid(string input, string expected)
    {
        Assert.Equal(expected, await RunAsync(input));
    }

    [Theory]
    [InlineData("guid seq")]
    [InlineData("guid seq abc")]
    [InlineData("guid seq 340282366920938463463374607431768211456")]
    [InlineData("guid repeat 12")]
    [InlineData("guid repeat g")]
    [InlineData("guid unknown")]
    public async Task InvalidInput_Throws(string input)
    {
        await Assert.ThrowsAsync<CommandInputException>(() => RunAsync(input));
    }
}
