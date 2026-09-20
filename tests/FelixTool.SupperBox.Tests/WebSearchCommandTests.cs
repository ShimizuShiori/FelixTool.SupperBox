namespace FelixTool.SupperBox.Tests;

using System.Text.Json;

public sealed class WebSearchCommandTests
{
    [Fact]
    public void BuildUrl_ReplacesAndEncodesNumberedKeywords()
    {
        var url = WebSearchCommand.BuildUrl(
            "https://www.foo.com?q={0}&type={1}&dd={2}",
            ["hello world", "a/b", "中文"]);

        Assert.Equal(
            "https://www.foo.com/?q=hello%20world&type=a%2Fb&dd=%E4%B8%AD%E6%96%87",
            url);
    }

    [Fact]
    public void BuildUrl_DoesNotTreatPercentEncodedCharactersAsPlaceholders()
    {
        var url = WebSearchCommand.BuildUrl(
            "https://github.com/search?q=org%3AWiseTechGlobal+{0}&type=repositories",
            ["SupperBox"]);

        Assert.Equal(
            "https://github.com/search?q=org%3AWiseTechGlobal+SupperBox&type=repositories",
            url);
    }

    [Fact]
    public void BuildUrl_RejectsMissingKeyword()
    {
        var exception = Assert.Throws<CommandInputException>(() =>
            WebSearchCommand.BuildUrl("https://www.foo.com?q={0}&type={1}", ["only one"]));

        Assert.Contains("需要 2 个关键词", exception.Message);
    }

    [Fact]
    public void BuildUrl_RejectsUnsafeScheme()
    {
        var exception = Assert.Throws<CommandInputException>(() =>
            WebSearchCommand.BuildUrl("file:///C:/Temp/{0}", ["test"]));

        Assert.Contains("HTTP", exception.Message);
    }

    [Fact]
    public void BuildUrl_ExplainsLegacyPlaceholderMigration()
    {
        var exception = Assert.Throws<CommandInputException>(() =>
            WebSearchCommand.BuildUrl("https://www.foo.com?q=%1", ["test"]));

        Assert.Contains("{0}", exception.Message);
    }

    [Fact]
    public void ApplyConfiguration_ProvidesEngineSuggestions()
    {
        var command = new WebSearchCommand(new RecordingLauncher());
        using var document = JsonDocument.Parse(
            """{"engine":{"foo":"https://www.foo.com?q={0}","bar":"https://bar.test/{0}"}}""");

        command.ApplyConfiguration(document.RootElement);
        var suggestions = command.GetSuggestions(0, "fo");

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("foo", suggestion.Value);
    }

    private sealed class RecordingLauncher : IExternalLauncher
    {
        public void Open(string target)
        {
        }
    }
}
