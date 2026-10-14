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
    Explorer,
    Everything
}

public sealed class RepositoryLauncher : IRepositoryLauncher
{
    public void Open(RepositoryRunner runner, string repositoryPath)
    {
        var startInfo = CreateStartInfo(runner, repositoryPath);
        if (runner is RepositoryRunner.Everything)
        {
            new EverythingWindowLifetime().Open(startInfo, repositoryPath);
        }
        else
        {
            using var process = Process.Start(startInfo);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(RepositoryRunner runner, string repositoryPath)
    {
        var startInfo = new ProcessStartInfo(runner switch
        {
            RepositoryRunner.Code => "code",
            RepositoryRunner.CommandPrompt => "cmd.exe",
            RepositoryRunner.Copilot => "copilot",
            RepositoryRunner.Explorer => "explorer.exe",
            RepositoryRunner.Everything => FindEverythingExecutable(),
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
        else if (runner is RepositoryRunner.Everything)
        {
            startInfo.ArgumentList.Add("-new-window");
            startInfo.ArgumentList.Add("-no-regex");
            startInfo.ArgumentList.Add("-path");
            startInfo.ArgumentList.Add(repositoryPath);
        }

        return startInfo;
    }

    private static string FindEverythingExecutable()
    {
        foreach (var folder in new[]
        {
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86
        })
        {
            var programFiles = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                var executable = Path.Combine(programFiles, "Everything", "Everything.exe");
                if (File.Exists(executable))
                {
                    return executable;
                }
            }
        }

        return "Everything.exe";
    }
}

public sealed class OpenWorkspaceRepositoryCommand(
    RepositoryCatalog repositories,
    IRepositoryLauncher launcher,
    IItemSelectionService itemSelection) : IOptionalConfigurableCommandPlugin, ICommandValueSuggestionProvider
{
    public string Name => "openrepo";

    public string Description => "使用指定程序打开已配置目录下的仓库";

    public string ConfigurationSectionName => "repositories";

    public void ApplyConfiguration(JsonElement configuration) => repositories.ApplyConfiguration(configuration);

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("runner", CommandParameterType.String, "打开方式：code、cmd、copilot、exp 或 everything"),
        new("repoName", CommandParameterType.String, "已配置仓库根目录下的仓库名称")
    ];

    public async Task<CommandResult> ExecuteAsync(
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
            "everything" => RepositoryRunner.Everything,
            _ => throw new CommandInputException(
                $"不支持 runner“{runnerName}”，可选值为 code、cmd、copilot、exp、everything。")
        };
        var repository = await repositories.SelectAsync(
            (string)arguments["repoName"]!, itemSelection, cancellationToken);
        if (repository is not null)
        {
            launcher.Open(runner, repository);
        }

        return CommandResult.Ok(string.Empty);
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
                ["exp"] = "在文件资源管理器中打开仓库",
                ["everything"] = "在 Everything 中搜索仓库目录，可继续追加搜索条件"
            }
                .Where(item => item.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(item => new CommandValueSuggestion(item.Key, item.Value))
                .ToList();
        }

        if (parameterIndex != 1)
        {
            return [];
        }

        return repositories.GetAll()
            .Select(Path.GetFileName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Select(name => new CommandValueSuggestion(name, "打开该仓库"))
            .ToList();
    }

}

public sealed class RepositoryCatalog(params string[] repositoryRoots)
{
    public const string DefaultRoot = @"C:\git\GitHub\WiseTechGlobal";

    private string[] _roots = repositoryRoots.Length == 0 ? [DefaultRoot] : repositoryRoots.ToArray();

    public void ApplyConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object
            || !configuration.TryGetProperty("roots", out var roots)
            || roots.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("repositories 配置中缺少 roots 数组。");
        }

        var paths = new List<string>();
        foreach (var root in roots.EnumerateArray())
        {
            if (root.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(root.GetString())
                || !Path.IsPathFullyQualified(root.GetString()!))
            {
                throw new InvalidDataException("repositories.roots 中的每一项必须是非空绝对目录路径。");
            }

            paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.GetString()!)));
        }

        Volatile.Write(ref _roots, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<string?> SelectAsync(
        string repositoryName,
        IItemSelectionService itemSelection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matches = GetAll().Where(path =>
            Path.GetFileName(path).Equals(repositoryName, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => await itemSelection.SelectAsync(
                "选择仓库",
                matches.Select(path => new SelectionItem<string>(
                    path, Path.GetFileName(path), path)).ToList(),
                cancellationToken)
        };
    }

    public IEnumerable<string> GetAll()
    {
        return Volatile.Read(ref _roots)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root))
            .Distinct(StringComparer.OrdinalIgnoreCase);
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

public sealed partial class QuickTextCommand(IOutputService output) :
    IVariadicCommandPlugin,
    ICommandValueSuggestionProvider,
    IOptionalConfigurableCommandPlugin
{
    private IReadOnlyDictionary<string, string> _quickTexts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Name => "quicktext";

    public string ConfigurationSectionName => "quicktexts";

    public string Description => "生成配置文件中定义的快捷文本";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("type", CommandParameterType.String, "配置文件 quicktexts 中的快捷文本类型")
    ];

    public CommandParameter RemainingParameter { get; } =
        new("arguments", CommandParameterType.String, "依次替换快捷文本中的 {0}、{1}、...");

    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var type = (string)arguments["type"]!;
        if (!_quickTexts.TryGetValue(type, out var format))
        {
            throw new CommandInputException($"找不到快捷文本类型“{type}”。请运行 sysconfig 进行配置。");
        }

        var values = (IReadOnlyList<object>)arguments["arguments"]!;
        var text = BuildText(format, values.Select(value => (string)value).ToList());
        await output.ShowAsync(text, cancellationToken);
        return CommandResult.Ok($"已生成 {type} 快捷文本。");
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex != 0)
        {
            return [];
        }

        var quickTexts = Volatile.Read(ref _quickTexts);
        return quickTexts.Keys
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Select(name => new CommandValueSuggestion(name, quickTexts[name]))
            .ToList();
    }

    public void ApplyConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("quicktexts 配置必须是包含快捷文本类型的对象。");
        }

        var quickTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in configuration.EnumerateObject())
        {
            var format = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(format))
            {
                throw new InvalidDataException("快捷文本类型和内容不能为空。");
            }

            quickTexts.Add(property.Name, format);
        }

        Volatile.Write(ref _quickTexts, quickTexts);
    }

    public static string BuildText(string format, IReadOnlyList<string> arguments)
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

public sealed class OpenPageCommand(IExternalLauncher launcher) :
    IOptionalConfigurableCommandPlugin,
    ICommandValueSuggestionProvider
{
    private IReadOnlyDictionary<string, string> _pages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Name => "openpage";

    public string ConfigurationSectionName => "pages";

    public string Description => "使用默认浏览器打开配置的页面";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("pageName", CommandParameterType.String, "配置文件 pages 中的页面名称")
    ];

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var pageName = (string)arguments["pageName"]!;
        if (!_pages.TryGetValue(pageName, out var url))
        {
            throw new CommandInputException($"找不到页面“{pageName}”。请运行 sysconfig 进行配置。");
        }

        launcher.Open(url);
        return Task.FromResult(CommandResult.Ok($"已打开页面 {pageName}。"));
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex != 0)
        {
            return [];
        }

        var pages = Volatile.Read(ref _pages);
        return pages.Keys
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Select(name => new CommandValueSuggestion(name, pages[name]))
            .ToList();
    }

    public void ApplyConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("pages 配置必须是包含页面名称和地址的对象。");
        }

        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in configuration.EnumerateObject())
        {
            var url = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidDataException("页面名称和地址不能为空。");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidDataException($"页面“{property.Name}”必须配置有效的 HTTP 或 HTTPS 地址。");
            }

            if (!pages.TryAdd(property.Name, url))
            {
                throw new InvalidDataException($"页面名称“{property.Name}”重复。");
            }
        }

        Volatile.Write(ref _pages, pages);
    }
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
