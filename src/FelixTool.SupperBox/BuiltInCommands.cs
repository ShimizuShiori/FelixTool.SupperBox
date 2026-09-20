using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public interface IExternalLauncher
{
    void Open(string target);
}

public sealed class ExternalLauncher : IExternalLauncher
{
    public void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}

public sealed partial class WebSearchCommand(IExternalLauncher launcher) :
    IVariadicCommandPlugin,
    ICommandValueSuggestionProvider,
    IConfigurableCommandPlugin
{
    private IReadOnlyDictionary<string, string> _engines =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Name => "websearch";

    public string Description => "使用已配置的搜索引擎打开网页搜索";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("engine", CommandParameterType.String, "配置文件中的搜索引擎名称")
    ];

    public CommandParameter RemainingParameter { get; } =
        new("keywords", CommandParameterType.String, "依次替换格式中的 {0}、{1}、...");

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var engine = (string)arguments["engine"]!;
        if (!_engines.TryGetValue(engine, out var format))
        {
            throw new CommandInputException($"找不到搜索引擎“{engine}”。请运行 sysconfig 进行配置。");
        }

        var keywords = (IReadOnlyList<object>)arguments["keywords"]!;
        var url = BuildUrl(format, keywords.Select(value => (string)value).ToList());
        launcher.Open(url);
        return Task.FromResult(CommandResult.Ok($"已使用 {engine} 打开搜索。"));
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex != 0)
        {
            return [];
        }

        var engines = Volatile.Read(ref _engines);
        return engines.Keys
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Select(name => new CommandValueSuggestion(name, engines[name]))
            .ToList();
    }

    public void ApplyConfiguration(JsonElement configuration)
    {
        if (!configuration.TryGetProperty("engine", out var engineElement)
            || engineElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("websearch 配置中缺少 engine 对象。");
        }

        var engines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in engineElement.EnumerateObject())
        {
            var format = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(format))
            {
                throw new InvalidDataException("搜索引擎名称和格式不能为空。");
            }

            engines.Add(property.Name, format);
        }

        Volatile.Write(ref _engines, engines);
    }

    public static string BuildUrl(string format, IReadOnlyList<string> keywords)
    {
        if (LegacyPlaceholderRegex().IsMatch(format))
        {
            throw new CommandInputException("搜索引擎格式不再支持 %1 占位符，请改用 {0}、{1}、...。");
        }

        var requiredKeywordCount = PlaceholderRegex().Matches(format)
            .Select(match => int.Parse(match.Groups[1].Value) + 1)
            .DefaultIfEmpty(0)
            .Max();
        if (keywords.Count < requiredKeywordCount)
        {
            throw new CommandInputException(
                $"该搜索引擎需要 {requiredKeywordCount} 个关键词，但只收到了 {keywords.Count} 个。");
        }

        var url = PlaceholderRegex().Replace(
            format,
            match => Uri.EscapeDataString(keywords[int.Parse(match.Groups[1].Value)]));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new CommandInputException("搜索引擎格式必须生成有效的 HTTP 或 HTTPS 地址。");
        }

        return uri.AbsoluteUri;
    }

    [GeneratedRegex(@"\{([0-9]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex("%[1-9](?![0-9A-Fa-f])", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyPlaceholderRegex();
}

public sealed class SystemConfigCommand(
    SystemConfiguration configuration,
    IExternalLauncher launcher) : ICommandPlugin
{
    public string Name => "sysconfig";

    public string Description => "打开 SupperBox 配置文件";

    public IReadOnlyList<CommandParameter> Parameters => [];

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        launcher.Open(configuration.FilePath);
        return Task.FromResult(CommandResult.Ok($"已打开配置文件：{configuration.FilePath}"));
    }
}
