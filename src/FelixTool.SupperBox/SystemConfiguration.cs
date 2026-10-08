using System.IO;
using System.Text.Json;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public sealed class SystemConfiguration : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly FileSystemWatcher _watcher;
    private readonly object _reloadLock = new();
    private CancellationTokenSource? _reloadCancellation;
    private JsonElement _root;
    private IReadOnlyList<IConfigurableCommandPlugin> _plugins = [];

    public SystemConfiguration(string? applicationDataDirectory = null)
    {
        var root = applicationDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(root, "FelixTool.SupperBox");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "config.json");

        if (!File.Exists(FilePath))
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(CreateDefault(), SerializerOptions));
        }

        _root = LoadFile();
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += ConfigurationChanged;
        _watcher.Created += ConfigurationChanged;
        _watcher.Renamed += ConfigurationChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public string FilePath { get; }

    public event EventHandler<ConfigurationReloadedEventArgs>? Reloaded;

    public void Bind(IEnumerable<ICommandPlugin> commands)
    {
        _plugins = commands.OfType<IConfigurableCommandPlugin>().ToList();
        Apply(_root);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        lock (_reloadLock)
        {
            _reloadCancellation?.Cancel();
            _reloadCancellation?.Dispose();
            _reloadCancellation = null;
        }
    }

    private void ConfigurationChanged(object sender, FileSystemEventArgs e)
    {
        CancellationTokenSource cancellation;
        lock (_reloadLock)
        {
            _reloadCancellation?.Cancel();
            _reloadCancellation?.Dispose();
            _reloadCancellation = cancellation = new CancellationTokenSource();
        }

        _ = ReloadAfterDelayAsync(cancellation.Token);
    }

    private async Task ReloadAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            var root = LoadFile();
            Apply(root);
            _root = root;
            Reloaded?.Invoke(this, new(true, $"已重新加载 {_plugins.Count} 个插件的配置。"));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Reloaded?.Invoke(this, new(false, $"配置重新加载失败：{exception.GetBaseException().Message}"));
        }
    }

    private JsonElement LoadFile()
    {
        using var stream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("配置文件根节点必须是 JSON 对象。");
        }

        return document.RootElement.Clone();
    }

    private void Apply(JsonElement root)
    {
        foreach (var plugin in _plugins)
        {
            var section = root.EnumerateObject()
                .FirstOrDefault(property =>
                    property.Name.Equals(
                        plugin.ConfigurationSectionName,
                        StringComparison.OrdinalIgnoreCase));
            if (section.Value.ValueKind == JsonValueKind.Undefined)
            {
                if (plugin is IOptionalConfigurableCommandPlugin)
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"配置中缺少插件“{plugin.Name}”对应的根属性“{plugin.ConfigurationSectionName}”。");
            }

            plugin.ApplyConfiguration(section.Value);
        }
    }

    private static object CreateDefault() => new
    {
        websearch = new
        {
            engine = new Dictionary<string, string>
            {
                ["bing"] = "https://www.bing.com/search?q={0}",
                ["google"] = "https://www.google.com/search?q={0}"
            }
        },
        quicktexts = new Dictionary<string, string>
        {
            ["translate"] = "Translate the following text into {0}: {1}"
        },
        pages = new Dictionary<string, string>
        {
            ["baidu"] = "https://www.baidu.com/"
        }
    };
}

public sealed class ConfigurationReloadedEventArgs(bool success, string message) : EventArgs
{
    public bool Success { get; } = success;

    public string Message { get; } = message;
}
