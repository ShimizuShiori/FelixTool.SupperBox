using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public interface IRunCommandLauncher
{
    void Run(string command, string workingDirectory);
}

public sealed class RunCommandLauncher : IRunCommandLauncher
{
    public void Run(string command, string workingDirectory)
    {
        Process.Start(CreateStartInfo(command, workingDirectory));
    }

    internal static ProcessStartInfo CreateStartInfo(string command, string workingDirectory) =>
        new("cmd.exe")
        {
            // /s removes the outer quotes while preserving quotes inside the command.
            Arguments = $"/d /s /k \"{command}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
}

public sealed class RunCommand(IRunCommandLauncher launcher) :
    IOptionalConfigurableCommandPlugin, ICommandValueSuggestionProvider
{
    private sealed record ConfiguredCommand(string Command, string? WorkingDirectory);

    private IReadOnlyDictionary<string, ConfiguredCommand> _commands =
        new Dictionary<string, ConfiguredCommand>(StringComparer.OrdinalIgnoreCase);

    public string Name => "run";

    public string ConfigurationSectionName => "commands";

    public string Description => "在 CMD 窗口中运行配置的命令，执行后保留窗口";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("target", CommandParameterType.String, "配置文件 commands 中的命令名称")
    ];

    public Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = (string)arguments["target"]!;
        if (!Volatile.Read(ref _commands).TryGetValue(target, out var settings))
        {
            throw new CommandInputException($"找不到命令目标“{target}”。请运行 sysconfig 进行配置。");
        }

        var workingDirectory = settings.WorkingDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(workingDirectory))
        {
            throw new CommandInputException($"命令目标“{target}”的工作目录不存在：{workingDirectory}");
        }

        launcher.Run(settings.Command, workingDirectory);
        return Task.FromResult(CommandResult.Ok($"已在 CMD 窗口启动：{target}"));
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex != 0)
        {
            return [];
        }

        return Volatile.Read(ref _commands)
            .Where(item => item.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Key.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Key)
            .Select(item => new CommandValueSuggestion(item.Key, item.Value.Command))
            .ToList();
    }

    public void ApplyConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("commands 配置必须是包含命令目标的对象。");
        }

        var commands = new Dictionary<string, ConfiguredCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in configuration.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name)
                || property.Value.ValueKind != JsonValueKind.Object
                || !property.Value.TryGetProperty("command", out var command)
                || command.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(command.GetString()))
            {
                throw new InvalidDataException(
                    $"commands 中的目标“{property.Name}”必须包含非空 command 字符串，且目标名称不能为空。");
            }

            string? workingDirectory = null;
            if (property.Value.TryGetProperty("workingDir", out var directory))
            {
                if (directory.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(directory.GetString())
                    || !Path.IsPathFullyQualified(directory.GetString()!))
                {
                    throw new InvalidDataException(
                        $"命令目标“{property.Name}”的 workingDir 必须是非空绝对目录路径。");
                }

                workingDirectory = Path.GetFullPath(directory.GetString()!);
            }

            if (!commands.TryAdd(property.Name, new(command.GetString()!, workingDirectory)))
            {
                throw new InvalidDataException($"命令目标名称“{property.Name}”重复。");
            }
        }

        Volatile.Write(ref _commands, commands);
    }
}
