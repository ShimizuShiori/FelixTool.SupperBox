using System.IO;
using System.Text.Json;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

public sealed class QuickTextCommandTests
{
    [Fact]
    public void BuildText_ReplacesProvidedArgumentsAndPreservesMissingPlaceholders()
    {
        var text = QuickTextCommand.BuildText(
            "Review {0} for {1}. Keep {2} and {10}.",
            ["this code", "security"]);

        Assert.Equal(
            "Review this code for security. Keep {2} and {10}.",
            text);
    }

    [Fact]
    public async Task ExecuteAsync_ShowsGeneratedText()
    {
        var output = new RecordingOutputService();
        var command = new QuickTextCommand(output);
        using var document = JsonDocument.Parse(
            """{"review":"Review {0}. Additional context: {1}"}""");
        command.ApplyConfiguration(document.RootElement);
        var registry = new CommandRegistry([command]);

        await registry.ExecuteAsync("quicktext review code", CancellationToken.None);

        Assert.Equal("Review code. Additional context: {1}", output.Content);
        Assert.False(registry.TryGet("prompt", out _));
        Assert.False(registry.TryGet("prompts", out _));
    }

    [Fact]
    public async Task ExecuteAsync_JoinsArrayLinesAndPreservesFormatting()
    {
        var output = new RecordingOutputService();
        var command = new QuickTextCommand(output);
        using var document = JsonDocument.Parse(
            """{"review":["Review {0}.", "", "  Additional context: {1}\nKeep this.\r\n", ""],"short":"Short {0}"}""");
        command.ApplyConfiguration(document.RootElement);
        var registry = new CommandRegistry([command]);

        await registry.ExecuteAsync("quicktext review code", CancellationToken.None);

        var expected = $"Review code.{Environment.NewLine}{Environment.NewLine}  Additional context: {{1}}\nKeep this.\r\n{Environment.NewLine}";
        Assert.Equal(expected, output.Content);
        await registry.ExecuteAsync("quicktext short code", CancellationToken.None);
        Assert.Equal("Short code", output.Content);
    }

    [Theory]
    [InlineData("""["Review {0}"]""", false)]
    [InlineData("""["", "Review {0}", ""]""", true)]
    public async Task ExecuteAsync_AcceptsSingleLineAndSurroundingBlankLines(string template, bool surroundingBlankLines)
    {
        var output = new RecordingOutputService();
        var command = new QuickTextCommand(output);
        using var document = JsonDocument.Parse($$"""{"review":{{template}}}""");

        command.ApplyConfiguration(document.RootElement);
        var registry = new CommandRegistry([command]);

        await registry.ExecuteAsync("quicktext review code", CancellationToken.None);

        var expected = surroundingBlankLines
            ? $"{Environment.NewLine}Review code{Environment.NewLine}"
            : "Review code";
        Assert.Equal(expected, output.Content);
        Assert.Equal(expected.Replace("code", "{0}"),
            Assert.Single(command.GetSuggestions(0, "review")).Description);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""["", " \t"]""")]
    [InlineData("""["Valid", null]""")]
    [InlineData("""["Valid", 42]""")]
    [InlineData("""["Valid", false]""")]
    [InlineData("""["Valid", {}]""")]
    [InlineData("""["Valid", ["nested"]]""")]
    public void ApplyConfiguration_RejectsInvalidTemplatesAndPreservesPreviousConfiguration(string template)
    {
        var command = new QuickTextCommand(new RecordingOutputService());
        using var original = JsonDocument.Parse("""{"review":"Original"}""");
        command.ApplyConfiguration(original.RootElement);
        using var invalid = JsonDocument.Parse($$"""{"short":"New","review":{{template}}}""");

        Assert.Throws<InvalidDataException>(() => command.ApplyConfiguration(invalid.RootElement));

        var suggestion = Assert.Single(command.GetSuggestions(0, ""));
        Assert.Equal("review", suggestion.Value);
        Assert.Equal("Original", suggestion.Description);
    }

    [Fact]
    public void ApplyConfiguration_ProvidesTypeSuggestions()
    {
        var command = new QuickTextCommand(new RecordingOutputService());
        using var document = JsonDocument.Parse(
            """{"review":"Review {0}","translate":"Translate {0}"}""");

        command.ApplyConfiguration(document.RootElement);
        var suggestion = Assert.Single(command.GetSuggestions(0, "rev"));

        Assert.Equal("review", suggestion.Value);
    }

    [Theory]
    [InlineData("quicktexts", true)]
    [InlineData("QUICKTEXTS", true)]
    [InlineData("quicktext", false)]
    [InlineData("prompts", false)]
    public void Bind_ReadsOnlyQuickTextsSection(string sectionName, bool expectedSuggestion)
    {
        var applicationData = Path.Combine(
            Path.GetTempPath(),
            $"SupperBoxTests-{Guid.NewGuid():N}");
        var configurationDirectory = Path.Combine(applicationData, "FelixTool.SupperBox");
        Directory.CreateDirectory(configurationDirectory);
        File.WriteAllText(
            Path.Combine(configurationDirectory, "config.json"),
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [sectionName] = new Dictionary<string, string> { ["review"] = "Review {0}" }
            }));

        try
        {
            using var configuration = new SystemConfiguration(applicationData);
            var command = new QuickTextCommand(new RecordingOutputService());

            configuration.Bind([command]);

            Assert.Equal(expectedSuggestion, command.GetSuggestions(0, "review").Count == 1);
        }
        finally
        {
            Directory.Delete(applicationData, true);
        }
    }

    [Fact]
    public async Task DefaultConfiguration_ProvidesQuickTexts()
    {
        var applicationData = Path.Combine(
            Path.GetTempPath(),
            $"SupperBoxTests-{Guid.NewGuid():N}");
        try
        {
            using var configuration = new SystemConfiguration(applicationData);
            using var document = JsonDocument.Parse(File.ReadAllText(configuration.FilePath));
            Assert.True(document.RootElement.TryGetProperty("quicktexts", out _));
            Assert.False(document.RootElement.TryGetProperty("prompts", out _));
            var output = new RecordingOutputService();
            var command = new QuickTextCommand(output);
            configuration.Bind([command]);
            var registry = new CommandRegistry([command]);

            await registry.ExecuteAsync(
                """quicktext translate Chinese "Hello world" """,
                CancellationToken.None);

            Assert.Equal("Translate the following text into Chinese: Hello world", output.Content);
        }
        finally
        {
            Directory.Delete(applicationData, true);
        }
    }

    private sealed class RecordingOutputService : IOutputService
    {
        public string? Content { get; private set; }

        public Task ShowAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            return Task.CompletedTask;
        }
    }
}
