namespace FelixTool.SupperBox.Abstractions;

public enum CommandParameterType
{
    String,
    Integer,
    Number,
    Boolean
}

public sealed record CommandParameter(
    string Name,
    CommandParameterType Type,
    string Description,
    bool Required = true,
    string? DefaultValue = null);

public sealed record CommandResult(bool Success, string Message)
{
    public static CommandResult Ok(string message) => new(true, message);

    public static CommandResult Error(string message) => new(false, message);
}

public interface ICommandPlugin
{
    string Name { get; }

    string Description { get; }

    IReadOnlyList<CommandParameter> Parameters { get; }

    Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);
}
