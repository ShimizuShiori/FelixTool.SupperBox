using System.IO;
using System.Text.Json;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

public sealed class SystemConfigurationTests
{
    [Fact]
    public void NewConfiguration_IncludesDefaultRepositoryRoot()
    {
        var applicationData = Path.Combine(Path.GetTempPath(), $"SupperBoxTests-{Guid.NewGuid():N}");
        try
        {
            using var configuration = new SystemConfiguration(applicationData);
            using var document = JsonDocument.Parse(File.ReadAllText(configuration.FilePath));

            Assert.Equal(
                [RepositoryCatalog.DefaultRoot],
                document.RootElement.GetProperty("repositories").GetProperty("roots")
                    .EnumerateArray().Select(root => root.GetString()));
        }
        finally
        {
            Directory.Delete(applicationData, true);
        }
    }

    [Fact]
    public async Task Bind_RepositoryCommandsShareConfigurationAndHotReload()
    {
        var applicationData = Path.Combine(Path.GetTempPath(), $"SupperBoxTests-{Guid.NewGuid():N}");
        var configurationDirectory = Path.Combine(applicationData, "FelixTool.SupperBox");
        Directory.CreateDirectory(configurationDirectory);
        var filePath = Path.Combine(configurationDirectory, "config.json");
        var firstRoot = Path.Combine(applicationData, "First");
        var secondRoot = Path.Combine(applicationData, "Second");
        Directory.CreateDirectory(Path.Combine(firstRoot, "Alpha"));
        Directory.CreateDirectory(Path.Combine(secondRoot, "Beta"));
        File.WriteAllText(filePath, JsonSerializer.Serialize(new
        {
            repositories = new { roots = new[] { firstRoot, secondRoot } }
        }));

        try
        {
            using var configuration = new SystemConfiguration(applicationData);
            var catalog = new RepositoryCatalog();
            var selection = new RecordingRepositorySelection();
            var open = new OpenWorkspaceRepositoryCommand(catalog, new UnusedRepositoryLauncher(), selection);
            var branch = new NewWiBranchCommand(catalog, new UnusedGitRepositoryService(), selection);
            configuration.Bind([open, branch]);

            Assert.Equal(["Alpha", "Beta"], open.GetSuggestions(1, "").Select(item => item.Value));
            Assert.Equal(["Alpha", "Beta"], branch.GetSuggestions(0, "").Select(item => item.Value));

            var reloaded = new TaskCompletionSource<ConfigurationReloadedEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            configuration.Reloaded += (_, args) => reloaded.TrySetResult(args);
            File.WriteAllText(filePath, JsonSerializer.Serialize(new
            {
                repositories = new { roots = new[] { secondRoot } }
            }));

            var result = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(result.Success, result.Message);
            Assert.Equal(["Beta"], open.GetSuggestions(1, "").Select(item => item.Value));
            Assert.Equal(["Beta"], branch.GetSuggestions(0, "").Select(item => item.Value));
        }
        finally
        {
            Directory.Delete(applicationData, true);
        }
    }

    [Fact]
    public void Bind_MissingRepositorySectionPreservesExistingRoots()
    {
        var applicationData = Path.Combine(Path.GetTempPath(), $"SupperBoxTests-{Guid.NewGuid():N}");
        var configurationDirectory = Path.Combine(applicationData, "FelixTool.SupperBox");
        Directory.CreateDirectory(configurationDirectory);
        Directory.CreateDirectory(Path.Combine(applicationData, "Alpha"));
        File.WriteAllText(Path.Combine(configurationDirectory, "config.json"), "{}");
        try
        {
            using var configuration = new SystemConfiguration(applicationData);
            var catalog = new RepositoryCatalog(applicationData);
            var selection = new RecordingRepositorySelection();
            var open = new OpenWorkspaceRepositoryCommand(catalog, new UnusedRepositoryLauncher(), selection);
            var branch = new NewWiBranchCommand(catalog, new UnusedGitRepositoryService(), selection);

            configuration.Bind([open, branch]);

            Assert.Single(open.GetSuggestions(1, "Alpha"));
            Assert.Single(branch.GetSuggestions(0, "Alpha"));
        }
        finally
        {
            Directory.Delete(applicationData, true);
        }
    }

    [Fact]
    public void Bind_DispatchesRootPropertyByPluginNameIgnoringCase()
    {
        var applicationData = Path.Combine(
            Path.GetTempPath(),
            $"SupperBoxTests-{Guid.NewGuid():N}");
        var configurationDirectory = Path.Combine(applicationData, "FelixTool.SupperBox");
        Directory.CreateDirectory(configurationDirectory);
        File.WriteAllText(
            Path.Combine(configurationDirectory, "config.json"),
            """{"DEMO":{"value":"configured"}}""");

        try
        {
            using var configuration = new SystemConfiguration(applicationData);
            var plugin = new ConfigurableCommand();

            configuration.Bind([plugin]);

            Assert.Equal("configured", plugin.Value);
        }
        finally
        {
            Directory.Delete(applicationData, true);
        }
    }

    [Fact]
    public void Bind_DispatchesExplicitConfigurationSectionName()
    {
        var applicationData = Path.Combine(
            Path.GetTempPath(),
            $"SupperBoxTests-{Guid.NewGuid():N}");
        var configurationDirectory = Path.Combine(applicationData, "FelixTool.SupperBox");
        Directory.CreateDirectory(configurationDirectory);
        File.WriteAllText(
            Path.Combine(configurationDirectory, "config.json"),
            """{"PAGES":{"value":"configured"}}""");

        try
        {
            using var configuration = new SystemConfiguration(applicationData);
            var plugin = new ConfigurableCommand("openpage", "pages");

            configuration.Bind([plugin]);

            Assert.Equal("configured", plugin.Value);
        }
        finally
        {
            Directory.Delete(applicationData, true);
        }
    }

    private sealed class UnusedRepositoryLauncher : IRepositoryLauncher
    {
        public void Open(RepositoryRunner runner, string repositoryPath) =>
            throw new InvalidOperationException("Configuration must not open a repository.");
    }

    private sealed class UnusedGitRepositoryService : IGitRepositoryService
    {
        public Task<string> CreateBranchFromLatestMainAsync(
            string repositoryPath,
            string branchName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Configuration must not create a branch.");
    }

    private sealed class ConfigurableCommand(
        string name = "demo",
        string? configurationSectionName = null) : IConfigurableCommandPlugin
    {
        public string Name => name;
        public string ConfigurationSectionName => configurationSectionName ?? Name;
        public string Description => "Configurable";
        public IReadOnlyList<CommandParameter> Parameters => [];
        public string? Value { get; private set; }

        public void ApplyConfiguration(JsonElement configuration)
        {
            Value = configuration.GetProperty("value").GetString();
        }

        public Task<CommandResult> ExecuteAsync(
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CommandResult.Ok("done"));
        }
    }
}
