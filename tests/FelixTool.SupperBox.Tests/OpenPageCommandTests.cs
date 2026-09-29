using System.IO;
using System.Text.Json;

namespace FelixTool.SupperBox.Tests;

public sealed class OpenPageCommandTests
{
    [Fact]
    public async Task ExecuteAsync_OpensConfiguredPageWithDefaultLauncher()
    {
        var launcher = new RecordingLauncher();
        var command = new OpenPageCommand(launcher);
        using var document = JsonDocument.Parse(
            """{"baidu":"https://www.baidu.com/","docs":"https://example.test/docs"}""");
        command.ApplyConfiguration(document.RootElement);
        var registry = new CommandRegistry([command]);

        var result = await registry.ExecuteAsync("openpage docs", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://example.test/docs", launcher.Target);
    }

    [Fact]
    public void ApplyConfiguration_ProvidesPageNameSuggestions()
    {
        var command = new OpenPageCommand(new RecordingLauncher());
        using var document = JsonDocument.Parse(
            """{"baidu":"https://www.baidu.com/","docs":"https://example.test/docs"}""");

        command.ApplyConfiguration(document.RootElement);
        var suggestion = Assert.Single(command.GetSuggestions(0, "doc"));

        Assert.Equal("docs", suggestion.Value);
        Assert.Equal("https://example.test/docs", suggestion.Description);
    }

    [Fact]
    public void ApplyConfiguration_RejectsNonHttpAddress()
    {
        var command = new OpenPageCommand(new RecordingLauncher());
        using var document = JsonDocument.Parse("""{"local":"file:///C:/Temp/page.html"}""");

        var exception = Assert.Throws<InvalidDataException>(
            () => command.ApplyConfiguration(document.RootElement));

        Assert.Contains("HTTP", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ExplainsUnknownPage()
    {
        var command = new OpenPageCommand(new RecordingLauncher());
        using var document = JsonDocument.Parse("""{"docs":"https://example.test/docs"}""");
        command.ApplyConfiguration(document.RootElement);
        var registry = new CommandRegistry([command]);

        var exception = await Assert.ThrowsAsync<CommandInputException>(
            () => registry.ExecuteAsync("openpage missing", CancellationToken.None));

        Assert.Contains("找不到页面", exception.Message);
    }

    private sealed class RecordingLauncher : IExternalLauncher
    {
        public string? Target { get; private set; }

        public void Open(string target)
        {
            Target = target;
        }
    }
}
