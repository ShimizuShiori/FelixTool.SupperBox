using FelixTool.SupperBox.Abstractions;
using FelixTool.SupperBox.SamplePlugin;

namespace FelixTool.SupperBox.Tests;

public sealed class CommandRegistryTests
{
    [Fact]
    public async Task ExecuteAsync_ConvertsTypedArguments()
    {
        var registry = new CommandRegistry([new AddCommand()]);

        var result = await registry.ExecuteAsync("add 1.5 2", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("1.5 + 2 = 3.5", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ExplainsInvalidArgument()
    {
        var registry = new CommandRegistry([new AddCommand()]);

        var exception = await Assert.ThrowsAsync<CommandInputException>(
            () => registry.ExecuteAsync("add nope 2", CancellationToken.None));

        Assert.Contains("left", exception.Message);
        Assert.Contains("数字", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesPluginFailureForUiBoundary()
    {
        var registry = new CommandRegistry([new ThrowingCommand()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ExecuteAsync("fail", CancellationToken.None));

        Assert.Equal("sample failure", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PassesRemainingArgumentsToVariadicCommand()
    {
        var command = new VariadicCommand();
        var registry = new CommandRegistry([command]);

        await registry.ExecuteAsync("many first second third", CancellationToken.None);

        Assert.Equal(["second", "third"], command.Remaining);
    }

    private sealed class ThrowingCommand : ICommandPlugin
    {
        public string Name => "fail";

        public string Description => "Throws";

        public IReadOnlyList<CommandParameter> Parameters => [];

        public Task<CommandResult> ExecuteAsync(
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("sample failure");
        }
    }

    private sealed class VariadicCommand : IVariadicCommandPlugin
    {
        public string Name => "many";
        public string Description => "Variadic";
        public IReadOnlyList<CommandParameter> Parameters { get; } =
            [new("first", CommandParameterType.String, "First")];
        public CommandParameter RemainingParameter { get; } =
            new("remaining", CommandParameterType.String, "Remaining");
        public IReadOnlyList<string> Remaining { get; private set; } = [];

        public Task<CommandResult> ExecuteAsync(
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            Remaining = ((IReadOnlyList<object>)arguments["remaining"]!)
                .Cast<string>()
                .ToList();
            return Task.FromResult(CommandResult.Ok("done"));
        }
    }
}
