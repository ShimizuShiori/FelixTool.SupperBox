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

public interface IRepositoryLauncher
{
    void Open(RepositoryRunner runner, string repositoryPath);
}

public enum RepositoryRunner
{
    Code,
    CommandPrompt,
    Copilot,
    Explorer
}

public sealed class RepositoryLauncher : IRepositoryLauncher
{
    public void Open(RepositoryRunner runner, string repositoryPath)
    {
        Process.Start(CreateStartInfo(runner, repositoryPath));
    }

    internal static ProcessStartInfo CreateStartInfo(RepositoryRunner runner, string repositoryPath)
    {
        var startInfo = new ProcessStartInfo(runner switch
        {
            RepositoryRunner.Code => "code",
            RepositoryRunner.CommandPrompt => "cmd.exe",
            RepositoryRunner.Copilot => "copilot",
            RepositoryRunner.Explorer => "explorer.exe",
            _ => throw new ArgumentOutOfRangeException(nameof(runner), runner, null)
        })
        {
            WorkingDirectory = repositoryPath,
            UseShellExecute = true
        };
        if (runner is RepositoryRunner.Code or RepositoryRunner.Explorer)
        {
            startInfo.ArgumentList.Add(repositoryPath);
        }

        return startInfo;
    }
}

public sealed class OpenWorkspaceRepositoryCommand(
    string repositoryRoot,
    IRepositoryLauncher launcher) : ICommandPlugin, ICommandValueSuggestionProvider
{
    private readonly RepositoryCatalog _repositories = new(repositoryRoot);

    public string Name => "openrepo";

    public string Description => "使用指定程序打开 WiseTechGlobal 仓库";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("runner", CommandParameterType.String, "打开方式：code、cmd、copilot 或 exp"),
        new("repoName", CommandParameterType.String, "公司代码仓库根目录下的仓库名称")
    ];

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var runnerName = (string)arguments["runner"]!;
        var runner = runnerName.ToLowerInvariant() switch
        {
            "code" => RepositoryRunner.Code,
            "cmd" => RepositoryRunner.CommandPrompt,
            "copilot" => RepositoryRunner.Copilot,
            "exp" => RepositoryRunner.Explorer,
            _ => throw new CommandInputException(
                $"不支持 runner“{runnerName}”，可选值为 code、cmd、copilot、exp。")
        };
        var repository = _repositories.Find((string)arguments["repoName"]!);
        if (repository is not null)
        {
            launcher.Open(runner, repository);
        }

        return Task.FromResult(CommandResult.Ok(string.Empty));
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex == 0)
        {
            return new Dictionary<string, string>
            {
                ["code"] = "使用 VS Code 打开仓库",
                ["cmd"] = "在仓库目录打开命令提示符",
                ["copilot"] = "在仓库目录启动 Copilot",
                ["exp"] = "在文件资源管理器中打开仓库"
            }
                .Where(item => item.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(item => new CommandValueSuggestion(item.Key, item.Value))
                .ToList();
        }

        if (parameterIndex != 1)
        {
            return [];
        }

        return _repositories.GetAll()
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Select(name => new CommandValueSuggestion(name, "打开该仓库"))
            .ToList();
    }

}

public sealed class RepositoryCatalog(string repositoryRoot)
{
    public string? Find(string repositoryName)
    {
        return GetAll().FirstOrDefault(path =>
            Path.GetFileName(path).Equals(repositoryName, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> GetAll()
    {
        return Directory.Exists(repositoryRoot)
            ? Directory.EnumerateDirectories(repositoryRoot)
            : [];
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

public sealed partial class PromptsCommand(IOutputService output) :
    IVariadicCommandPlugin,
    ICommandValueSuggestionProvider,
    IOptionalConfigurableCommandPlugin
{
    private IReadOnlyDictionary<string, string> _prompts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Name => "prompts";

    public string Description => "生成配置文件中定义的提示词";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("type", CommandParameterType.String, "配置文件 prompts 中的提示词类型")
    ];

    public CommandParameter RemainingParameter { get; } =
        new("arguments", CommandParameterType.String, "依次替换提示词中的 {0}、{1}、...");

    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var type = (string)arguments["type"]!;
        if (!_prompts.TryGetValue(type, out var format))
        {
            throw new CommandInputException($"找不到提示词类型“{type}”。请运行 sysconfig 进行配置。");
        }

        var values = (IReadOnlyList<object>)arguments["arguments"]!;
        var prompt = BuildPrompt(format, values.Select(value => (string)value).ToList());
        await output.ShowAsync(prompt, cancellationToken);
        return CommandResult.Ok($"已生成 {type} 提示词。");
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex != 0)
        {
            return [];
        }

        var prompts = Volatile.Read(ref _prompts);
        return prompts.Keys
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Select(name => new CommandValueSuggestion(name, prompts[name]))
            .ToList();
    }

    public void ApplyConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("prompts 配置必须是包含提示词类型的对象。");
        }

        var prompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in configuration.EnumerateObject())
        {
            var format = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(format))
            {
                throw new InvalidDataException("提示词类型和内容不能为空。");
            }

            prompts.Add(property.Name, format);
        }

        Volatile.Write(ref _prompts, prompts);
    }

    public static string BuildPrompt(string format, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(arguments);

        return PlaceholderRegex().Replace(format, match =>
        {
            var index = int.Parse(match.Groups[1].Value);
            return index < arguments.Count ? arguments[index] : match.Value;
        });
    }

    [GeneratedRegex(@"\{([0-9]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
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
