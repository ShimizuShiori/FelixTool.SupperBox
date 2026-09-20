using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public interface IVisualStudioLocator
{
    string? Find();
}

public sealed class VisualStudioLocator : IVisualStudioLocator
{
    public string? Find()
    {
        var installDirectory = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (!string.IsNullOrWhiteSpace(installDirectory))
        {
            var environmentCandidate = Path.Combine(installDirectory, "Common7", "IDE", "devenv.exe");
            if (File.Exists(environmentCandidate))
            {
                return environmentCandidate;
            }
        }

        foreach (var vsWherePath in GetVsWherePaths())
        {
            var productPath = FindWithVsWhere(vsWherePath);
            if (productPath is not null)
            {
                return productPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetVsWherePaths()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(
                programFilesX86,
                "Microsoft Visual Studio",
                "Installer",
                "vswhere.exe");
        }

        yield return "vswhere.exe";
    }

    private static string? FindWithVsWhere(string vsWherePath)
    {
        if (Path.IsPathFullyQualified(vsWherePath) && !File.Exists(vsWherePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(vsWherePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-latest");
        startInfo.ArgumentList.Add("-prerelease");
        startInfo.ArgumentList.Add("-requires");
        startInfo.ArgumentList.Add("Microsoft.VisualStudio.Component.CoreEditor");
        startInfo.ArgumentList.Add("-property");
        startInfo.ArgumentList.Add("productPath");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return null;
            }

            return output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(path =>
                    Path.GetFileName(path).Equals("devenv.exe", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path));
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}

public interface ISolutionLauncher
{
    void Open(string visualStudioPath, string solutionPath);
}

public sealed class VisualStudioSolutionLauncher : ISolutionLauncher
{
    public void Open(string visualStudioPath, string solutionPath)
    {
        var startInfo = new ProcessStartInfo(visualStudioPath)
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(solutionPath);
        Process.Start(startInfo);
    }
}

public sealed class OpenSolutionCommand(
    IItemSelectionService itemSelection,
    IVisualStudioLocator visualStudioLocator,
    ISolutionLauncher launcher) : ICommandPlugin
{
    public string Name => "opensln";

    public string Description => "从指定位置向上查找并使用 Visual Studio 打开解决方案";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("filepath", CommandParameterType.String, "作为向上查找起点的文件或目录")
    ];

    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var solutions = FindSolutions((string)arguments["filepath"]!, cancellationToken);
        if (solutions.Count == 0)
        {
            throw new CommandInputException("从指定位置向上未找到 .sln 或 .slnx 解决方案文件。");
        }

        var solution = solutions.Count == 1
            ? solutions[0]
            : await itemSelection.SelectAsync(
                "选择要打开的 Visual Studio 解决方案",
                solutions.Select(path => new SelectionItem<string>(
                    path,
                    Path.GetFileName(path),
                    path)).ToList(),
                cancellationToken);

        var visualStudioPath = visualStudioLocator.Find();
        if (visualStudioPath is null)
        {
            throw new CommandInputException(
                "未找到 Visual Studio。请确认已安装 Visual Studio Installer，或已设置 VSINSTALLDIR。");
        }

        launcher.Open(visualStudioPath, solution);
        return CommandResult.Ok($"已使用 Visual Studio 打开：{solution}");
    }

    public static IReadOnlyList<string> FindSolutions(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        DirectoryInfo? directory;
        if (Directory.Exists(fullPath))
        {
            directory = new DirectoryInfo(fullPath);
        }
        else if (File.Exists(fullPath))
        {
            directory = new FileInfo(fullPath).Directory;
        }
        else
        {
            throw new CommandInputException($"文件或目录不存在：{fullPath}");
        }

        var solutions = new List<string>();
        while (directory is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            solutions.AddRange(directory
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(file => file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                    || file.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => file.FullName));
            directory = directory.Parent;
        }

        return solutions;
    }
}
