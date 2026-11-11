using System.Globalization;
using System.Numerics;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public interface IGuidStrategy
{
    string Name { get; }

    string Description { get; }

    bool RequiresArgument { get; }

    Guid Generate(string? argument);
}

public sealed class RandomGuidStrategy : IGuidStrategy
{
    public string Name => "random";

    public string Description => "生成一个随机 GUID";

    public bool RequiresArgument => false;

    public Guid Generate(string? argument) => Guid.NewGuid();
}

public sealed class SequentialGuidStrategy : IGuidStrategy
{
    private const int MaxDigits = 32;

    public string Name => "seq";

    public string Description => "把十进制数字转为十六进制并右对齐写入 GUID，其余补 0，如 seq 10 -> ...000A";

    public bool RequiresArgument => true;

    public Guid Generate(string? argument)
    {
        if (string.IsNullOrEmpty(argument)
            || !argument.All(char.IsAsciiDigit)
            || !BigInteger.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            throw new CommandInputException("seq 需要十进制非负整数。");
        }

        var hex = number.ToString("X", CultureInfo.InvariantCulture).TrimStart('0').PadLeft(32, '0');
        if (hex.Length > 32)
        {
            throw new CommandInputException("seq 的数值超出 GUID 的 128 位范围。");
        }

        return Guid.ParseExact(hex, "N");
    }
}

public sealed class RepeatGuidStrategy : IGuidStrategy
{
    public string Name => "repeat";

    public string Description => "用同一个十六进制字符填满 GUID，如 repeat 1 -> 11111111-...";

    public bool RequiresArgument => true;

    public Guid Generate(string? argument)
    {
        if (argument is not { Length: 1 } || !char.IsAsciiHexDigit(argument[0]))
        {
            throw new CommandInputException("repeat 需要单个十六进制字符（0-9、A-F）。");
        }

        return Guid.ParseExact(new string(argument[0], 32), "N");
    }
}

public sealed class GuidCommand(
    IOutputService output,
    IEnumerable<IGuidStrategy>? strategies = null) :
    ICommandPlugin,
    ICommandValueSuggestionProvider
{
    private readonly IReadOnlyList<IGuidStrategy> _strategies = (strategies ??
    [
        new RandomGuidStrategy(),
        new SequentialGuidStrategy(),
        new RepeatGuidStrategy()
    ]).ToList();

    public string Name => "guid";

    public string Description => "按策略生成 GUID（random / seq / repeat）";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("strategy", CommandParameterType.String, "生成策略：random、seq、repeat"),
        new("value", CommandParameterType.String, "策略参数：seq 的十进制数字，repeat 的十六进制字符", false)
    ];

    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var strategyName = (string)arguments["strategy"]!;
        var strategy = _strategies.FirstOrDefault(item =>
            item.Name.Equals(strategyName, StringComparison.OrdinalIgnoreCase))
            ?? throw new CommandInputException(
                $"找不到 GUID 策略“{strategyName}”。可用策略：{string.Join("、", _strategies.Select(item => item.Name))}。");

        var value = (string?)arguments["value"];
        if (strategy.RequiresArgument && string.IsNullOrEmpty(value))
        {
            throw new CommandInputException($"策略“{strategy.Name}”需要参数。");
        }

        var guid = strategy.Generate(value);
        await output.ShowAsync(guid.ToString("D").ToUpperInvariant(), cancellationToken);
        return CommandResult.Ok($"已生成 GUID：{guid.ToString("D").ToUpperInvariant()}");
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex == 0)
        {
            return _strategies
                .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.Name)
                .Select(item => new CommandValueSuggestion(item.Name, item.Description))
                .ToList();
        }

        return [];
    }
}
