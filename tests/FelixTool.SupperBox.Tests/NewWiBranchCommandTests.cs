using System.IO;

namespace FelixTool.SupperBox.Tests;

public sealed class NewWiBranchCommandTests : IDisposable
{
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        $"SupperBoxRepositories-{Guid.NewGuid():N}");

    public NewWiBranchCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "Glow"));
    }

    public void Dispose()
    {
        Directory.Delete(_repositoryRoot, true);
    }

    [Fact]
    public void BuildBranchName_NormalizesSpecialCharactersAndWhitespace()
    {
        var branchName = NewWiBranchCommand.BuildBranchName(
            ["WI01095616", "-", "Front", "end", "observability", "HLD", "is", "completed"]);

        Assert.Equal("FLF/WI01095616-Front-end-observability-HLD-is-completed", branchName);
    }

    [Fact]
    public void BuildBranchName_TruncatesCompleteNameToOneHundredCharacters()
    {
        var branchName = NewWiBranchCommand.BuildBranchName([new string('a', 120)]);

        Assert.Equal(100, branchName.Length);
        Assert.StartsWith("FLF/", branchName);
    }

    [Fact]
    public async Task RegistryExecuteAsync_UsesAllRemainingTextAndCreatesBranchInRepository()
    {
        var git = new RecordingGitRepositoryService();
        var command = new NewWiBranchCommand(
            new RepositoryCatalog(_repositoryRoot), git, new RecordingRepositorySelection());
        var registry = new CommandRegistry([command]);

        var result = await registry.ExecuteAsync(
            "newwibranch glow WI01095616 - Front end observability HLD is completed",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(_repositoryRoot, "Glow"), git.RepositoryPath);
        Assert.Equal(
            "FLF/WI01095616-Front-end-observability-HLD-is-completed",
            git.BranchName);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingWorkItemTitle()
    {
        var command = new NewWiBranchCommand(
            new RepositoryCatalog(_repositoryRoot),
            new RecordingGitRepositoryService(),
            new RecordingRepositorySelection());
        var registry = new CommandRegistry([command]);

        await Assert.ThrowsAsync<CommandInputException>(() =>
            registry.ExecuteAsync("newwibranch Glow", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_SelectsDuplicateRepositoryBeforeCreatingBranch()
    {
        var secondRoot = Path.Combine(_repositoryRoot, "OtherRoot");
        var secondRepository = Directory.CreateDirectory(Path.Combine(secondRoot, "Glow")).FullName;
        var git = new RecordingGitRepositoryService();
        var selection = new RecordingRepositorySelection { SelectedIndex = 1 };
        var command = new NewWiBranchCommand(
            new RepositoryCatalog(_repositoryRoot, secondRoot), git, selection);
        var registry = new CommandRegistry([command]);

        await registry.ExecuteAsync("newwibranch Glow WI01095616 title", CancellationToken.None);

        Assert.Equal(secondRepository, git.RepositoryPath);
        Assert.Equal(1, selection.CallCount);
        Assert.All(selection.Items, item => Assert.Equal(item.Value, item.Description));
        Assert.Single(command.GetSuggestions(0, "glow"));
    }

    [Fact]
    public async Task ExecuteAsync_CancelingSelectionDoesNotCreateBranch()
    {
        var secondRoot = Path.Combine(_repositoryRoot, "OtherRoot");
        Directory.CreateDirectory(Path.Combine(secondRoot, "Glow"));
        var git = new RecordingGitRepositoryService();
        var command = new NewWiBranchCommand(
            new RepositoryCatalog(_repositoryRoot, secondRoot),
            git,
            new RecordingRepositorySelection { Cancel = true });
        var registry = new CommandRegistry([command]);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            registry.ExecuteAsync("newwibranch Glow WI01095616 title", CancellationToken.None));

        Assert.Null(git.RepositoryPath);
    }

    [Theory]
    [InlineData(0, "master")]
    [InlineData(1, "main")]
    public async Task GitRepositoryService_UpdatesPreferredMainBranchBeforeCreatingBranch(
        int masterLookupExitCode,
        string expectedMainBranch)
    {
        var runner = new RecordingGitProcessRunner(masterLookupExitCode);
        var service = new GitRepositoryService(runner);

        var mainBranch = await service.CreateBranchFromLatestMainAsync(
            @"C:\repos\Glow",
            "FLF/WI01095616-title",
            CancellationToken.None);

        Assert.Equal(expectedMainBranch, mainBranch);
        Assert.Equal(
            [
                "show-ref --verify --quiet refs/heads/master",
                $"checkout {expectedMainBranch}",
                "pull",
                "checkout -b FLF/WI01095616-title"
            ],
            runner.Commands);
    }

    private sealed class RecordingGitRepositoryService : IGitRepositoryService
    {
        public string? RepositoryPath { get; private set; }

        public string? BranchName { get; private set; }

        public Task<string> CreateBranchFromLatestMainAsync(
            string repositoryPath,
            string branchName,
            CancellationToken cancellationToken)
        {
            RepositoryPath = repositoryPath;
            BranchName = branchName;
            return Task.FromResult("master");
        }
    }

    private sealed class RecordingGitProcessRunner(int masterLookupExitCode) : IGitProcessRunner
    {
        public List<string> Commands { get; } = [];

        public Task<GitCommandResult> RunAsync(
            string repositoryPath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Commands.Add(string.Join(' ', arguments));
            var exitCode = Commands.Count == 1 ? masterLookupExitCode : 0;
            return Task.FromResult(new GitCommandResult(exitCode, string.Empty, string.Empty));
        }
    }
}