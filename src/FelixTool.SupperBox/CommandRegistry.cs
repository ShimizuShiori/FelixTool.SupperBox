using System.Globalization;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public sealed class CommandRegistry(IEnumerable<ICommandPlugin> plugins)
{
    private readonly Dictionary<string, ICommandPlugin> _commands = plugins.ToDictionary(
        plugin => plugin.Name,
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ICommandPlugin> Commands => _commands.Values;

    public IReadOnlyList<ICommandPlugin> Find(string query)
    {
        return _commands.Values
            .Where(command => string.IsNullOrWhiteSpace(query)
                || command.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(command => command.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(command => command.Name)
            .ToList();
    }

    public async Task<CommandResult> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        var tokens = CommandParser.Tokenize(input);
        if (tokens.Count == 0)
        {
            throw new CommandInputException("请输入命令。");
        }

        if (!_commands.TryGetValue(tokens[0], out var command))
        {
            throw new CommandInputException($"找不到命令“{tokens[0]}”。");
        }

        var supplied = tokens.Skip(1).ToList();
        if (supplied.Count > command.Parameters.Count)
        {
            throw new CommandInputException(
                $"命令“{command.Name}”最多接受 {command.Parameters.Count} 个参数，但收到了 {supplied.Count} 个。");
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < command.Parameters.Count; index++)
        {
            var parameter = command.Parameters[index];
            var rawValue = index < supplied.Count ? supplied[index] : parameter.DefaultValue;
            if (rawValue is null)
            {
                if (parameter.Required)
                {
                    throw new CommandInputException($"缺少参数“{parameter.Name}”（{parameter.Description}）。");
                }

                arguments[parameter.Name] = null;
                continue;
            }

            arguments[parameter.Name] = ConvertValue(command.Name, parameter, rawValue);
        }

        return await command.ExecuteAsync(arguments, cancellationToken);
    }

    private static object ConvertValue(string commandName, CommandParameter parameter, string value)
    {
        object? converted = parameter.Type switch
        {
            CommandParameterType.String => value,
            CommandParameterType.Integer when int.TryParse(
                value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => integer,
            CommandParameterType.Number when double.TryParse(
                value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            CommandParameterType.Boolean when bool.TryParse(value, out var boolean) => boolean,
            _ => null
        };

        return converted ?? throw new CommandInputException(
            $"命令“{commandName}”的参数“{parameter.Name}”需要 {GetTypeName(parameter.Type)}，实际为“{value}”。");
    }

    public static string GetTypeName(CommandParameterType type) => type switch
    {
        CommandParameterType.String => "文本",
        CommandParameterType.Integer => "整数",
        CommandParameterType.Number => "数字",
        CommandParameterType.Boolean => "布尔值 (true/false)",
        _ => type.ToString()
    };
}
