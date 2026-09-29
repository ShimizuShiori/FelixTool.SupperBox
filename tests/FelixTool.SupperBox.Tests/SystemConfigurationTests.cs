using System.IO;
using System.Text.Json;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

public sealed class SystemConfigurationTests
{
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
