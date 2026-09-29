using System.Text.Json;

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

public interface IVariadicCommandPlugin : ICommandPlugin
{
    CommandParameter RemainingParameter { get; }
}

public sealed record CommandValueSuggestion(string Value, string Description);

public interface ICommandValueSuggestionProvider
{
    IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query);
}

public interface IConfigurableCommandPlugin : ICommandPlugin
{
    void ApplyConfiguration(JsonElement configuration);
}

public interface IOptionalConfigurableCommandPlugin : IConfigurableCommandPlugin
{
}

public sealed record SelectionItem<T>(
    T Value,
    string Label,
    string Description = "");

public interface IItemSelectionService
{
    Task<T> SelectAsync<T>(
        string title,
        IReadOnlyList<SelectionItem<T>> items,
        CancellationToken cancellationToken);
}

public interface IOutputService
{
    Task ShowAsync(string content, CancellationToken cancellationToken);
}

public interface IPluginContext
{
    IItemSelectionService ItemSelection { get; }

    IOutputService Output { get; }
}

public interface IPluginContextAware
{
    void Initialize(IPluginContext context);
}
