using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public interface IGitRepositoryService
{
    Task<string> CreateBranchFromLatestMainAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken);
}

internal sealed record GitCommandResult(int ExitCode, string Output, string Error);

internal interface IGitProcessRunner
{
    Task<GitCommandResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed class GitProcessRunner : IGitProcessRunner
{
    public async Task<GitCommandResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git。请确认 git 已安装并已加入 PATH。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new GitCommandResult(process.ExitCode, await outputTask, await errorTask);
    }
}

public sealed class GitRepositoryService : IGitRepositoryService
{
    private readonly IGitProcessRunner _runner;

    public GitRepositoryService() : this(new GitProcessRunner())
    {
    }

    internal GitRepositoryService(IGitProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<string> CreateBranchFromLatestMainAsync(
        string repositoryPath,
        string branchName,
        CancellationToken cancellationToken)
    {
        var masterExists = await _runner.RunAsync(
            repositoryPath,
            ["show-ref", "--verify", "--quiet", "refs/heads/master"],
            cancellationToken);
        var mainBranch = masterExists.ExitCode == 0 ? "master" : "main";

        await RunRequiredAsync(repositoryPath, ["checkout", mainBranch], cancellationToken);
        await RunRequiredAsync(repositoryPath, ["pull"], cancellationToken);
        await RunRequiredAsync(repositoryPath, ["checkout", "-b", branchName], cancellationToken);
        return mainBranch;
    }

    private async Task RunRequiredAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(repositoryPath, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} 执行失败：{result.Error.Trim()}");
        }
    }

}

public sealed partial class NewWiBranchCommand(
    string repositoryRoot,
    IGitRepositoryService git) : IVariadicCommandPlugin, ICommandValueSuggestionProvider
{
    private const string BranchPrefix = "FLF/";
    private const int MaximumBranchNameLength = 100;
    private readonly RepositoryCatalog _repositories = new(repositoryRoot);

    public string Name => "newwibranch";

    public string Description => "从仓库最新主分支创建 FLF 工作项分支";

    public IReadOnlyList<CommandParameter> Parameters { get; } =
    [
        new("repoName", CommandParameterType.String, "公司代码仓库根目录下的仓库名称")
    ];

    public CommandParameter RemainingParameter { get; } =
        new("workItem", CommandParameterType.String, "工作项名称和标题");

    public async Task<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var repositoryName = (string)arguments["repoName"]!;
        var repository = _repositories.Find(repositoryName);
        if (repository is null)
        {
            throw new CommandInputException($"找不到仓库“{repositoryName}”。");
        }

        var workItemParts = (IReadOnlyList<object>)arguments["workItem"]!;
        var branchName = BuildBranchName(workItemParts.Cast<string>());
        var mainBranch = await git.CreateBranchFromLatestMainAsync(
            repository,
            branchName,
            cancellationToken);
        return CommandResult.Ok($"已从 {mainBranch} 创建分支 {branchName}。");
    }

    public IReadOnlyList<CommandValueSuggestion> GetSuggestions(int parameterIndex, string query)
    {
        if (parameterIndex != 0)
        {
            return [];
        }

        return _repositories.GetAll()
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Select(name => new CommandValueSuggestion(name, "在该仓库创建工作项分支"))
            .ToList();
    }

    public static string BuildBranchName(IEnumerable<string> workItemParts)
    {
        var workItem = string.Join(' ', workItemParts).Trim();
        var normalized = InvalidBranchCharactersRegex().Replace(workItem, "-").Trim('-');
        if (normalized.Length == 0)
        {
            throw new CommandInputException("工作项名称和标题必须包含字母或数字。");
        }

        var maximumSuffixLength = MaximumBranchNameLength - BranchPrefix.Length;
        if (normalized.Length > maximumSuffixLength)
        {
            normalized = normalized[..maximumSuffixLength].TrimEnd('-');
        }

        return BranchPrefix + normalized;
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidBranchCharactersRegex();
}