using System.Text.Json;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

public sealed class PromptsCommandTests
{
    [Fact]
    public void BuildPrompt_ReplacesProvidedArgumentsAndPreservesMissingPlaceholders()
    {
        var prompt = PromptsCommand.BuildPrompt(
            "Review {0} for {1}. Keep {2} and {10}.",
            ["this code", "security"]);

        Assert.Equal(
            "Review this code for security. Keep {2} and {10}.",
            prompt);
    }

    [Fact]
    public async Task ExecuteAsync_ShowsGeneratedPrompt()
    {
        var output = new RecordingOutputService();
        var command = new PromptsCommand(output);
        using var document = JsonDocument.Parse(
            """{"review":"Review {0}. Additional context: {1}"}""");
        command.ApplyConfiguration(document.RootElement);
        var registry = new CommandRegistry([command]);

        await registry.ExecuteAsync("prompts review code", CancellationToken.None);

        Assert.Equal("Review code. Additional context: {1}", output.Content);
    }

    [Fact]
    public void ApplyConfiguration_ProvidesTypeSuggestions()
    {
        var command = new PromptsCommand(new RecordingOutputService());
        using var document = JsonDocument.Parse(
            """{"review":"Review {0}","translate":"Translate {0}"}""");

        command.ApplyConfiguration(document.RootElement);
        var suggestion = Assert.Single(command.GetSuggestions(0, "rev"));

        Assert.Equal("review", suggestion.Value);
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
