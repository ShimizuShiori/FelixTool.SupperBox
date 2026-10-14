using System.IO;
using System.Text.RegularExpressions;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox;

public interface IRepositoryRemoteLauncher
{
    Task OpenAsync(
        string repositoryPath,
        IItemSelectionService itemSelection,
        CancellationToken cancellationToken);
}

public sealed partial class RepositoryRemoteLauncher : IRepositoryRemoteLauncher
{
    private readonly IExternalLauncher _launcher;
    private readonly IGitProcessRunner _git;

    public RepositoryRemoteLauncher(IExternalLauncher launcher) : this(launcher, new GitProcessRunner())
    {
    }

    internal RepositoryRemoteLauncher(IExternalLauncher launcher, IGitProcessRunner git)
    {
        _launcher = launcher;
        _git = git;
    }

    public async Task OpenAsync(
        string repositoryPath,
        IItemSelectionService itemSelection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = await RunRequiredAsync(repositoryPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            .Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CommandInputException("所选目录不是 Git 仓库的根目录。");
        }

        var output = await RunRequiredAsync(repositoryPath, ["remote"], cancellationToken);
        var remotes = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (remotes.Length == 0)
        {
            throw new CommandInputException("该仓库没有配置 remote。");
        }

        var remote = remotes.FirstOrDefault(name => name == "origin")
            ?? (remotes.Length == 1
                ? remotes[0]
                : await itemSelection.SelectAsync(
                    "选择仓库远端",
                    remotes.Select(name => new SelectionItem<string>(name, name)).ToList(),
                    cancellationToken));
        var url = await RunRequiredAsync(repositoryPath, ["remote", "get-url", "--", remote], cancellationToken);
        var webUrl = BuildWebUrl(url);
        cancellationToken.ThrowIfCancellationRequested();
        _launcher.Open(webUrl);
    }

    private async Task<string> RunRequiredAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new CommandInputException(
                $"git {string.Join(' ', arguments)} 执行失败：{result.Error.Trim()}");
        }

        return result.Output.Trim();
    }

    internal static string BuildWebUrl(string remoteUrl)
    {
        remoteUrl = remoteUrl.Trim();
        Uri? uri = null;
        if (!Path.IsPathFullyQualified(remoteUrl) && !WindowsDriveRegex().IsMatch(remoteUrl))
        {
            if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var parsed)
                && parsed.Scheme is "http" or "https" or "ssh" or "git")
            {
                uri = parsed;
            }
            else if (!remoteUrl.Contains("://", StringComparison.Ordinal))
            {
                var match = ScpUrlRegex().Match(remoteUrl);
                if (match.Success
                    && Uri.TryCreate(
                        $"https://{match.Groups["host"].Value}/{match.Groups["path"].Value.TrimStart('/')}",
                        UriKind.Absolute,
                        out var scpUri))
                {
                    uri = scpUri;
                }
            }
        }

        if (uri is null || string.IsNullOrEmpty(uri.Host))
        {
            throw new CommandInputException("无法将该 remote 地址转换为 HTTP 或 HTTPS 链接。");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        if (string.IsNullOrEmpty(path) || path == "/")
        {
            throw new CommandInputException("remote 地址中没有仓库路径。");
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
            Path = path
        };
        if (uri.Scheme is "ssh" or "git")
        {
            builder.Scheme = Uri.UriSchemeHttps;
            builder.Port = -1;
        }

        return builder.Uri.AbsoluteUri;
    }

    [GeneratedRegex(@"^[A-Za-z]:", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDriveRegex();

    [GeneratedRegex(@"^(?:[^/@:\s]+@)?(?<host>[A-Za-z0-9.-]+):(?<path>[^\\\s]+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ScpUrlRegex();
}
