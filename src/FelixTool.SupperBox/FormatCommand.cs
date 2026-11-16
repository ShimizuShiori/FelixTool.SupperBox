using System.Text;
using System.Text.RegularExpressions;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public interface IFormatHandler
{
    string SourceType { get; }

    string TargetType { get; }

    string Description { get; }

    string Format(string content);
}

public sealed partial class WiBranchFormatHandler : IFormatHandler
{
    private const string BranchPrefix = "FLF/";
    private const int MaximumBranchNameBytes = 100;

    public string SourceType => "wi";

    public string TargetType => "branch";

    public string Description => "将工作项文本或 Markdown 链接转换为 FLF Git 分支名";

    public string Format(string content)
    {
        var text = content.Trim();
        if (text.StartsWith('['))
        {
            var link = MarkdownLinkRegex().Match(text);
            if (!link.Success)
            {
                throw new CommandInputException("wi 需要工作项文本或完整的 HTTP/HTTPS Markdown 链接。");
            }

            text = link.Groups["text"].Value.Trim();
        }

        var workItem = WorkItemRegex().Match(text);
        if (!workItem.Success)
        {
            throw new CommandInputException("工作项格式应为 WI编号 - 标题。");
        }

        var branch = new StringBuilder(BranchPrefix)
            .Append(workItem.Groups["id"].Value.ToUpperInvariant())
            .Append('_');
        var byteCount = Encoding.UTF8.GetByteCount(branch.ToString());
        if (byteCount >= MaximumBranchNameBytes)
        {
            throw new CommandInputException("工作项编号过长，无法在分支名中保留标题。");
        }

        var hasTitle = false;
        foreach (var rune in workItem.Groups["title"].Value.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune))
            {
                continue;
            }

            if (byteCount + rune.Utf8SequenceLength > MaximumBranchNameBytes)
            {
                break;
            }

            branch.Append(rune.ToString());
            byteCount += rune.Utf8SequenceLength;
            hasTitle = true;
        }

        if (!hasTitle)
        {
            throw new CommandInputException("工作项标题必须包含字母或数字，且能够放入分支名长度限制内。");
        }

        return branch.ToString();
    }

    [GeneratedRegex(@"\A\[(?<text>[\s\S]*)\]\(https?://[^\s]+\)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"\A(?<id>WI[0-9]+)(?:\s*-\s*|\s+)(?<title>[\s\S]+)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex WorkItemRegex();
}

public sealed class FormatCommand : IVariadicCommandPlugin, ICommandValueSuggestionProvider
{
    private readonly IOutputService _output;
    private readonly Dictionary<string, Dictionary<string, IFormatHandler>> _handlers;

    public FormatCommand(IOutputService output, IEnumerable<IFormatHandler>? handlers = null)
    {
        _output = output;
        _handlers = (handlers ?? [new WiBranchFormatHandler()])
            .GroupBy(handler => handler.SourceType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(handler => handler.TargetType, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    public string Name => "format";

    public string Description => "按源类型和目标类型转换文本";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("sourcetype", CommandParameterType.String, "源类型，如 wi"),
        new("targettype", CommandParameterType.String, "该源类型支持的目标类型，如 branch")
    ];

    public CommandParameter RemainingParameter { get; } =
        new("content", CommandParameterType.String, "需要转换的文本或 Markdown 链接");

    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var sourceType = (string)arguments["sourcetype"]!;
        if (!_handlers.TryGetValue(sourceType, out var targets))
        {
            throw new CommandInputException(
                $"不支持源类型“{sourceType}”。可用类型：{string.Join("、", _handlers.Keys)}。");
        }

        var targetType = (string)arguments["targettype"]!;
        if (!targets.TryGetValue(targetType, out var handler))
        {
            throw new CommandInputException(
                $"源类型“{sourceType}”不支持目标类型“{targetType}”。可用类型：{string.Join("、", targets.Keys)}。");
        }

        var content = string.Join(' ', ((IReadOnlyList<object>)arguments["content"]!).Cast<string>());
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new CommandInputException("请提供需要转换的内容。");
        }

        var result = handler.Format(content);
        await _output.ShowAsync(result, cancellationToken);
        return CommandResult.Ok(result);
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query) =>
        GetSuggestions(parameterIndex, query, []);

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(
        int parameterIndex,
        string query,
        IReadOnlyList<string> precedingArguments)
    {
        IEnumerable<CommandValueSuggestion> suggestions;
        if (parameterIndex == 0)
        {
            suggestions = _handlers.Keys.Select(source => new CommandValueSuggestion(
                source, $"可转换为：{string.Join("、", _handlers[source].Keys)}"));
        }
        else if (parameterIndex == 1
            && precedingArguments.Count > 0
            && _handlers.TryGetValue(precedingArguments[0], out var targets))
        {
            suggestions = targets.Values.Select(handler =>
                new CommandValueSuggestion(handler.TargetType, handler.Description));
        }
        else
        {
            return [];
        }

        return suggestions
            .Where(item => item.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Value.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Value)
            .ToList();
    }
}
