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
    public void GetSuggestions_ReturnsMatchingRepositoryNamesWithPrefixesFirst()
    {
        var command = new OpenWorkspaceRepositoryCommand(
            _repositoryRoot,
            new RecordingRepositoryLauncher());

        Assert.Equal("openrepo", command.Name);
        var suggestions = command.GetSuggestions(0, "a");

        Assert.Equal(["Alpha", "Another", "Beta"], suggestions.Select(item => item.Value));
    }

    [Fact]
    public async Task ExecuteAsync_OpensExistingRepository()
    {
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(_repositoryRoot, launcher);

        var result = await command.ExecuteAsync(
            new Dictionary<string, object?> { ["repoName"] = "alpha" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(_repositoryRoot, "Alpha"), launcher.OpenedPath);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData(@"..\Alpha")]
    public async Task ExecuteAsync_DoesNothingForUnknownRepository(string repositoryName)
    {
        var launcher = new RecordingRepositoryLauncher();
        var command = new OpenWorkspaceRepositoryCommand(_repositoryRoot, launcher);

        var result = await command.ExecuteAsync(
            new Dictionary<string, object?> { ["repoName"] = repositoryName },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(launcher.OpenedPath);
    }

    private sealed class RecordingRepositoryLauncher : IRepositoryLauncher
    {
        public string? OpenedPath { get; private set; }

        public void Open(string repositoryPath)
        {
            OpenedPath = repositoryPath;
        }
    }
}
