using System.IO;

namespace FelixTool.SupperBox.Tests;

public sealed class OpenWorkspaceRepositoryCommandTests : IDisposable
{
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        $"SupperBoxRepositories-{Guid.NewGuid():N}");

    public OpenWorkspaceRepositoryCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Alpha"));
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Beta"));
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Another"));
    }

    public void Dispose()
    {
        Directory.Delete(_repositoryRoot, true);
    }

    [Fact]
    public void GetSuggestions_ReturnsRunnersAndMatchingRepositoryNames()
    {
        var command = new OpenWorkspaceRepositoryCommand(
            _repositoryRoot,
            new RecordingRepositoryLauncher());

        Assert.Equal("openrepo", command.Name);
        Assert.Equal(
            ["code", "cmd", "copilot", "exp"],
            command.GetSuggestions(0, string.Empty).Select(item => item.Value));

        var suggestions = command.GetSuggestions(1, "a");

        Assert.Equal(["Alpha", "Another", "Beta"], suggestions.Select(item => item.Value));
    }

    [Theory]
    [InlineData("code", RepositoryRunner.Code)]
    [InlineData("cmd", RepositoryRunner.CommandPrompt)]
    [InlineData("copilot", RepositoryRunner.Copilot)]
    [InlineData("exp", RepositoryRunner.Explorer)]
    public async Task ExecuteAsync_OpensExistingRepositoryWithSelectedRunner(
        string runner,
        RepositoryRunner expectedRunner)
    {
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(_repositoryRoot, launcher);

        var result = await command.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["runner"] = runner,
                ["repoName"] = "alpha"
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(expectedRunner, launcher.Runner);
        Assert.Equal(Path.Combine(_repositoryRoot, "Alpha"), launcher.OpenedPath);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnknownRunner()
    {
        var command = new OpenWorkspaceRepositoryCommand(
            _repositoryRoot,
            new RecordingRepositoryLauncher());

        var exception = await Assert.ThrowsAsync<CommandInputException>(() => command.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["runner"] = "unknown",
                ["repoName"] = "Alpha"
            },
            CancellationToken.None));

        Assert.Contains("unknown", exception.Message);
    }

    [Theory]
    [InlineData(RepositoryRunner.Code, "code", true)]
    [InlineData(RepositoryRunner.CommandPrompt, "cmd.exe", false)]
    [InlineData(RepositoryRunner.Copilot, "copilot", false)]
    [InlineData(RepositoryRunner.Explorer, "explorer.exe", true)]
    public void RepositoryLauncher_CreatesExpectedProcess(
        RepositoryRunner runner,
        string executable,
        bool passesRepositoryAsArgument)
    {
        var repositoryPath = Path.Combine(_repositoryRoot, "Alpha");

        var startInfo = RepositoryLauncher.CreateStartInfo(runner, repositoryPath);

        Assert.Equal(executable, startInfo.FileName);
        Assert.Equal(repositoryPath, startInfo.WorkingDirectory);
        Assert.Equal(passesRepositoryAsArgument ? [repositoryPath] : [], startInfo.ArgumentList);
        Assert.True(startInfo.UseShellExecute);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData(@"..\Alpha")]
    public async Task ExecuteAsync_DoesNothingForUnknownRepository(string repositoryName)
    {
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(_repositoryRoot, launcher);

        var result = await command.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["runner"] = "code",
                ["repoName"] = repositoryName
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(launcher.OpenedPath);
    }

    private sealed class RecordingRepositoryLauncher : IRepositoryLauncher
    {
        public RepositoryRunner? Runner { get; private set; }

        public string? OpenedPath { get; private set; }

        public void Open(RepositoryRunner runner, string repositoryPath)
        {
            Runner = runner;
            OpenedPath = repositoryPath;
        }
    }
}
