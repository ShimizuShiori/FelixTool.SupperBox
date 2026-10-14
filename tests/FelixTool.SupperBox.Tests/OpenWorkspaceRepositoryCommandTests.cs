using System.IO;
using FelixTool.SupperBox.Abstractions;

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
            new RepositoryCatalog(_repositoryRoot),
            new RecordingRepositoryLauncher(),
            new RecordingRepositorySelection());

        Assert.Equal("openrepo", command.Name);
        Assert.Equal(
            ["code", "cmd", "copilot", "exp", "everything", "remote"],
            command.GetSuggestions(0, string.Empty).Select(item => item.Value));

        var suggestions = command.GetSuggestions(1, "a");

        Assert.Equal(["Alpha", "Another", "Beta"], suggestions.Select(item => item.Value));
    }

    [Theory]
    [InlineData("code", RepositoryRunner.Code)]
    [InlineData("cmd", RepositoryRunner.CommandPrompt)]
    [InlineData("copilot", RepositoryRunner.Copilot)]
    [InlineData("exp", RepositoryRunner.Explorer)]
    [InlineData("everything", RepositoryRunner.Everything)]
    [InlineData("EVERYTHING", RepositoryRunner.Everything)]
    public async Task ExecuteAsync_OpensExistingRepositoryWithSelectedRunner(
        string runner,
        RepositoryRunner expectedRunner)
    {
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(
            new RepositoryCatalog(_repositoryRoot), launcher, new RecordingRepositorySelection());

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
            new RepositoryCatalog(_repositoryRoot),
            new RecordingRepositoryLauncher(),
            new RecordingRepositorySelection());

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
    [InlineData(@"C:\Repositories\Alpha")]
    [InlineData(@"C:\My Repositories\Alpha Beta")]
    [InlineData(@"C:\Repositories\Alpha\")]
    [InlineData(@"\\server\share\Alpha Beta")]
    public void RepositoryLauncher_EverythingSearchesWithinRepository(string repositoryPath)
    {
        var startInfo = RepositoryLauncher.CreateStartInfo(RepositoryRunner.Everything, repositoryPath);

        Assert.Equal("Everything.exe", Path.GetFileName(startInfo.FileName));
        Assert.Equal(repositoryPath, startInfo.WorkingDirectory);
        Assert.Equal(
            ["-new-window", "-no-regex", "-path", repositoryPath],
            startInfo.ArgumentList);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void GetSuggestions_FiltersEverythingRunnerCaseInsensitively()
    {
        var command = new OpenWorkspaceRepositoryCommand(
            new RepositoryCatalog(_repositoryRoot),
            new RecordingRepositoryLauncher(),
            new RecordingRepositorySelection());

        var suggestion = Assert.Single(command.GetSuggestions(0, "EVERY"));

        Assert.Equal("everything", suggestion.Value);
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("REMOTE")]
    public async Task ExecuteAsync_RemoteOpensWebPageInsteadOfRepositoryProcess(string runner)
    {
        var launcher = new RecordingRepositoryLauncher();
        var remoteLauncher = new RecordingRemoteLauncher();
        var selection = new RecordingRepositorySelection();
        var command = new OpenWorkspaceRepositoryCommand(
            new RepositoryCatalog(_repositoryRoot), launcher, selection, remoteLauncher);
        var registry = new CommandRegistry([command]);

        var result = await registry.ExecuteAsync($"openrepo {runner} alpha", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(_repositoryRoot, "Alpha"), remoteLauncher.RepositoryPath);
        Assert.Same(selection, remoteLauncher.ItemSelection);
        Assert.Null(launcher.OpenedPath);
        Assert.Equal("remote", Assert.Single(command.GetSuggestions(0, "REM")).Value);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData(@"..\Alpha")]
    public async Task ExecuteAsync_DoesNothingForUnknownRepository(string repositoryName)
    {
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(
            new RepositoryCatalog(_repositoryRoot), launcher, new RecordingRepositorySelection());

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

    [Fact]
    public async Task ExecuteAsync_SelectsDuplicateRepositoryAndDeduplicatesSuggestions()
    {
        var secondRoot = Path.Combine(_repositoryRoot, "OtherRoot");
        var secondRepository = Directory.CreateDirectory(Path.Combine(secondRoot, "Alpha")).FullName;
        var selection = new RecordingRepositorySelection { SelectedIndex = 1 };
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(
            new RepositoryCatalog(_repositoryRoot, secondRoot), launcher, selection);

        await command.ExecuteAsync(
            new Dictionary<string, object?> { ["runner"] = "code", ["repoName"] = "alpha" },
            CancellationToken.None);

        Assert.Equal(secondRepository, launcher.OpenedPath);
        Assert.Equal(1, selection.CallCount);
        Assert.Equal(
            [Path.Combine(_repositoryRoot, "Alpha"), secondRepository],
            selection.Items.Select(item => item.Value));
        Assert.All(selection.Items, item => Assert.Equal(item.Value, item.Description));
        Assert.Single(command.GetSuggestions(1, "alpha"));
    }

    [Fact]
    public async Task ExecuteAsync_CancelingSelectionDoesNotOpenRepository()
    {
        var secondRoot = Path.Combine(_repositoryRoot, "OtherRoot");
        Directory.CreateDirectory(Path.Combine(secondRoot, "Alpha"));
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(
            new RepositoryCatalog(_repositoryRoot, secondRoot),
            launcher,
            new RecordingRepositorySelection { Cancel = true });

        await Assert.ThrowsAsync<OperationCanceledException>(() => command.ExecuteAsync(
            new Dictionary<string, object?> { ["runner"] = "code", ["repoName"] = "Alpha" },
            CancellationToken.None));

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

    private sealed class RecordingRemoteLauncher : IRepositoryRemoteLauncher
    {
        public string? RepositoryPath { get; private set; }
        public IItemSelectionService? ItemSelection { get; private set; }

        public Task OpenAsync(
            string repositoryPath,
            IItemSelectionService itemSelection,
            CancellationToken cancellationToken)
        {
            RepositoryPath = repositoryPath;
            ItemSelection = itemSelection;
            return Task.CompletedTask;
        }
    }
}
