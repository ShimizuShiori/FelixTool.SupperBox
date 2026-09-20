using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.SamplePlugin;

public sealed class EchoCommand : ICommandPlugin
{
    public string Name => "echo";

    public string Description => "原样返回输入的文本";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("text", CommandParameterType.String, "要显示的文本")
    ];

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(CommandResult.Ok(arguments["text"]?.ToString() ?? string.Empty));
    }
}

public sealed class AddCommand : ICommandPlugin
{
    public string Name => "add";

    public string Description => "将两个数字相加";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("left", CommandParameterType.Number, "第一个数字"),
        new("right", CommandParameterType.Number, "第二个数字")
    ];

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var left = (double)arguments["left"]!;
        var right = (double)arguments["right"]!;
        return Task.FromResult(CommandResult.Ok($"{left} + {right} = {left + right}"));
    }
}
