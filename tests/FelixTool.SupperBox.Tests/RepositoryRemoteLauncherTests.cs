using System.IO;

namespace FelixTool.SupperBox.Tests;

public sealed class RepositoryRemoteLauncherTests
{
    private const string RepositoryPath = @"C:\Repositories\Alpha";

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("http://git.example.test:8080/team/repo.git/", "http://git.example.test:8080/team/repo")]
    [InlineData("git@github.com:owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("github.com:owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("git@git.example.test:/team/subgroup/repo.git", "https://git.example.test/team/subgroup/repo")]
    [InlineData("ssh://git@git.example.test:2222/team/repo.git", "https://git.example.test/team/repo")]
    [InlineData("git://git.example.test:9418/team/repo.git", "https://git.example.test/team/repo")]
    [InlineData(" https://git.example.test/team/repo.GIT \r\n", "https://git.example.test/team/repo")]
    [InlineData("https://user:password@git.example.test/team/repo.git?token=secret#fragment",
        "https://git.example.test/team/repo")]
    [InlineData("https://git.example.test/team/my%20repo.git", "https://git.example.test/team/my%20repo")]
    public void BuildWebUrl_ConvertsCommonRemoteFormats(string remoteUrl, string expected)
    {
        Assert.Equal(expected, RepositoryRemoteLauncher.BuildWebUrl(remoteUrl));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\Repositories\Alpha.git")]
    [InlineData(@"C:Alpha.git")]
    [InlineData(@"\\server\share\Alpha.git")]
    [InlineData("/srv/git/Alpha.git")]
    [InlineData("../Alpha.git")]
    [InlineData("file:///C:/Repositories/Alpha.git")]
    [InlineData("ftp://git.example.test/team/repo.git")]
    [InlineData("ext::git helper")]
    [InlineData("https://git.example.test/")]
    [InlineData("git@github.com:")]
    public void BuildWebUrl_RejectsUnsupportedOrIncompleteAddresses(string remoteUrl)
    {
        Assert.Throws<CommandInputException>(() => RepositoryRemoteLauncher.BuildWebUrl(remoteUrl));
    }

    [Fact]
    public async Task OpenAsync_PrefersOriginAndReadsFetchUrl()
    {
        var git = new RecordingGitRunner("fork\norigin\nupstream\n");
        var browser = new RecordingBrowser();
        var selection = new RecordingRepositorySelection();
        var launcher = new RepositoryRemoteLauncher(browser, git);

        await launcher.OpenAsync(RepositoryPath, selection, CancellationToken.None);

        Assert.Equal("https://github.com/owner/repo", browser.Target);
        Assert.Equal(
            ["rev-parse --show-toplevel", "remote", "remote get-url -- origin"],
            git.Commands);
        Assert.Equal(0, selection.CallCount);
        Assert.All(git.Repositories, path => Assert.Equal(RepositoryPath, path));
    }

    [Fact]
    public async Task OpenAsync_UsesOnlyRemoteWhenOriginIsAbsent()
    {
        var git = new RecordingGitRunner("upstream\n");
        var browser = new RecordingBrowser();
        var selection = new RecordingRepositorySelection();
        var launcher = new RepositoryRemoteLauncher(browser, git);

        await launcher.OpenAsync(RepositoryPath, selection, CancellationToken.None);

        Assert.Equal("remote get-url -- upstream", git.Commands.Last());
        Assert.NotNull(browser.Target);
        Assert.Equal(0, selection.CallCount);
    }

    [Fact]
    public async Task OpenAsync_SelectsBetweenMultipleRemotesWithoutOrigin()
    {
        var git = new RecordingGitRunner("fork\nupstream\n");
        var browser = new RecordingBrowser();
        var selection = new RecordingRepositorySelection { SelectedIndex = 1 };
        var launcher = new RepositoryRemoteLauncher(browser, git);

        await launcher.OpenAsync(RepositoryPath, selection, CancellationToken.None);

        Assert.Equal(["fork", "upstream"], selection.Items.Select(item => item.Label));
        Assert.Equal("remote get-url -- upstream", git.Commands.Last());
        Assert.NotNull(browser.Target);
    }

    [Fact]
    public async Task OpenAsync_CancelingRemoteSelectionDoesNotOpenBrowser()
    {
        var git = new RecordingGitRunner("fork\nupstream\n");
        var browser = new RecordingBrowser();
        var launcher = new RepositoryRemoteLauncher(browser, git);

        await Assert.ThrowsAsync<OperationCanceledException>(() => launcher.OpenAsync(
            RepositoryPath, new RecordingRepositorySelection { Cancel = true }, CancellationToken.None));

        Assert.Null(browser.Target);
        Assert.Equal(2, git.Commands.Count);
    }

    [Fact]
    public async Task OpenAsync_RejectsMissingRemotes()
    {
        var browser = new RecordingBrowser();
        var launcher = new RepositoryRemoteLauncher(browser, new RecordingGitRunner(string.Empty));

        var exception = await Assert.ThrowsAsync<CommandInputException>(() => launcher.OpenAsync(
            RepositoryPath, new RecordingRepositorySelection(), CancellationToken.None));

        Assert.Contains("没有配置 remote", exception.Message);
        Assert.Null(browser.Target);
    }

    [Fact]
    public async Task OpenAsync_RejectsDirectoryInsideAnotherRepository()
    {
        var browser = new RecordingBrowser();
        var git = new RecordingGitRunner("origin") { Root = @"C:\Repositories" };
        var launcher = new RepositoryRemoteLauncher(browser, git);

        await Assert.ThrowsAsync<CommandInputException>(() => launcher.OpenAsync(
            RepositoryPath, new RecordingRepositorySelection(), CancellationToken.None));

        Assert.Single(git.Commands);
        Assert.Null(browser.Target);
    }

    [Theory]
    [InlineData("rev-parse --show-toplevel")]
    [InlineData("remote")]
    [InlineData("remote get-url -- origin")]
    public async Task OpenAsync_ReportsGitFailures(string failingCommand)
    {
        var browser = new RecordingBrowser();
        var git = new RecordingGitRunner("origin") { FailingCommand = failingCommand };
        var launcher = new RepositoryRemoteLauncher(browser, git);

        var exception = await Assert.ThrowsAsync<CommandInputException>(() => launcher.OpenAsync(
            RepositoryPath, new RecordingRepositorySelection(), CancellationToken.None));

        Assert.Contains(failingCommand, exception.Message);
        Assert.Contains("Git failure", exception.Message);
        Assert.Null(browser.Target);
    }

    [Fact]
    public async Task OpenAsync_RejectsLocalRemoteWithoutOpeningBrowser()
    {
        var browser = new RecordingBrowser();
        var git = new RecordingGitRunner("origin") { Url = @"C:\Repositories\Other.git" };
        var launcher = new RepositoryRemoteLauncher(browser, git);

        await Assert.ThrowsAsync<CommandInputException>(() => launcher.OpenAsync(
            RepositoryPath, new RecordingRepositorySelection(), CancellationToken.None));

        Assert.Null(browser.Target);
    }

    [Fact]
    public async Task OpenAsync_PropagatesBrowserLaunchFailure()
    {
        var browser = new RecordingBrowser { Fail = true };
        var launcher = new RepositoryRemoteLauncher(browser, new RecordingGitRunner("origin"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.OpenAsync(
            RepositoryPath, new RecordingRepositorySelection(), CancellationToken.None));
    }

    [Fact]
    public async Task OpenAsync_PreCanceledRequestDoesNotRunGit()
    {
        var git = new RecordingGitRunner("origin");
        var browser = new RecordingBrowser();
        var launcher = new RepositoryRemoteLauncher(browser, git);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launcher.OpenAsync(
            RepositoryPath, new RecordingRepositorySelection(), new CancellationToken(true)));

        Assert.Empty(git.Commands);
        Assert.Null(browser.Target);
    }

    [Fact]
    public async Task OpenAsync_ReadsRealGitConfigurationIncludingUrlRewrite()
    {
        var repository = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), $"SupperBoxRemote-{Guid.NewGuid():N}")).FullName;
        try
        {
            var git = new GitProcessRunner();
            foreach (var arguments in new string[][]
            {
                ["init", "--quiet"],
                ["remote", "add", "origin", "test-alias:owner/repo.git"],
                ["config", "url.https://git.example.test/.insteadOf", "test-alias:"]
            })
            {
                var result = await git.RunAsync(repository, arguments, CancellationToken.None);
                Assert.True(result.ExitCode == 0, result.Error);
            }

            var browser = new RecordingBrowser();
            var launcher = new RepositoryRemoteLauncher(browser);

            await launcher.OpenAsync(repository, new RecordingRepositorySelection(), CancellationToken.None);

            Assert.Equal("https://git.example.test/owner/repo", browser.Target);
        }
        finally
        {
            Directory.Delete(repository, true);
        }
    }

    private sealed class RecordingBrowser : IExternalLauncher
    {
        public string? Target { get; private set; }
        public bool Fail { get; init; }

        public void Open(string target)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Browser failure");
            }

            Target = target;
        }
    }

    private sealed class RecordingGitRunner(string remotes) : IGitProcessRunner
    {
        public List<string> Commands { get; } = [];
        public List<string> Repositories { get; } = [];
        public string Root { get; init; } = RepositoryPath;
        public string Url { get; init; } = "git@github.com:owner/repo.git";
        public string? FailingCommand { get; init; }

        public Task<GitCommandResult> RunAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var command = string.Join(' ', arguments);
            Commands.Add(command);
            Repositories.Add(repositoryPath);
            var result = command == FailingCommand
                ? new GitCommandResult(1, string.Empty, "Git failure")
                : new GitCommandResult(0, arguments[0] == "rev-parse"
                    ? Root
                    : arguments.Count == 1 ? remotes : Url, string.Empty);
            return Task.FromResult(result);
        }
    }
}
