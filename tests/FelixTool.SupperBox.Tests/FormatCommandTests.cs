using System.Diagnostics;
using System.Text;
using FelixTool.SupperBox.Abstractions;

namespace FelixTool.SupperBox.Tests;

public sealed class FormatCommandTests
{
    private sealed class CapturingOutput : IOutputService
    {
        public string? Content { get; private set; }

        public Task ShowAsync(string content, CancellationToken cancellationToken)
        {
            Content = content;
            return Task.CompletedTask;
        }
    }

    private sealed record TestHandler(string SourceType, string TargetType) : IFormatHandler
    {
        public string Description => TargetType;

        public string Format(string content) => $"{SourceType}:{TargetType}:{content}";
    }

    private static async Task<string> RunAsync(string input)
    {
        var output = new CapturingOutput();
        var registry = new CommandRegistry([new FormatCommand(output)]);
        var result = await registry.ExecuteAsync(input, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(output.Content, result.Message);
        return output.Content!;
    }

    [Theory]
    [InlineData("format wi branch \"WI01151449 - Web Billing - [SYS] Enable Billing Authentication\"")]
    [InlineData("format wi branch WI01151449 - Web Billing - [SYS] Enable Billing Authentication")]
    [InlineData("FORMAT WI BRANCH \"wi01151449 - Web Billing - [SYS] Enable Billing Authentication\"")]
    [InlineData("format wi branch \"[WI01151449 - Web Billing - [SYS] Enable Billing Authentication](https://ediprod.cw.wisetechglobal.com/link/ShowEditForm/WorkItem/e3f3fa52-2778-4696-9f6c-294fc237b20b?lang=en-gb)\"")]
    public async Task WorkItemTextAndLink_GenerateExpectedBranch(string input)
    {
        Assert.Equal("FLF/WI01151449_WebBillingSYSEnableBillingAuthentication", await RunAsync(input));
    }

    [Theory]
    [InlineData("WI01151449 - Fix ~^:?*[]\\ slash / dots.. @{test} .lock", "FLF/WI01151449_Fixslashdotstestlock")]
    [InlineData("WI01151449 - 修复 𐐀 123 😀", "FLF/WI01151449_修复𐐀123")]
    [InlineData("[WI01151449 - [SYS] Fix](https://example.com/path(with-parentheses))", "FLF/WI01151449_SYSFix")]
    public async Task Branch_IsAcceptedByGit(string text, string expected)
    {
        var branch = new WiBranchFormatHandler().Format(text);
        Assert.Equal(expected, branch);
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("check-ref-format");
        startInfo.ArgumentList.Add("--branch");
        startInfo.ArgumentList.Add(branch);
        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    [Theory]
    [InlineData("a", 85)]
    [InlineData("界", 28)]
    [InlineData("𐐀", 21)]
    public async Task LongTitle_TruncatesOnlyTitleWithoutSplittingCharacters(string character, int retained)
    {
        var title = string.Concat(Enumerable.Repeat(character, 200));
        var branch = await RunAsync($"format wi branch \"WI01151449 - {title}\"");
        Assert.Equal("FLF/WI01151449_" + string.Concat(Enumerable.Repeat(character, retained)), branch);
        Assert.InRange(Encoding.UTF8.GetByteCount(branch), 97, 100);
    }

    [Theory]
    [InlineData("format unknown branch text")]
    [InlineData("format wi unknown text")]
    [InlineData("format wi branch")]
    [InlineData("format wi branch \" \"")]
    [InlineData("format wi branch \"not a work item\"")]
    [InlineData("format wi branch \"WI01151449 - !!!\"")]
    [InlineData("format wi branch \"WI01151449 - \"")]
    [InlineData("format wi branch \"[WI01151449 - Fix](not-a-url)\"")]
    [InlineData("format wi branch \"[WI01151449 - Fix](https://example.com\"")]
    public async Task InvalidInput_ThrowsWithoutOutput(string input)
    {
        var output = new CapturingOutput();
        var registry = new CommandRegistry([new FormatCommand(output)]);
        await Assert.ThrowsAsync<CommandInputException>(() =>
            registry.ExecuteAsync(input, CancellationToken.None));
        Assert.Null(output.Content);
    }

    [Fact]
    public void OversizedIdentifier_IsNotSilentlyTruncated()
    {
        Assert.Throws<CommandInputException>(() =>
            new WiBranchFormatHandler().Format("WI" + new string('1', 100) + " - Title"));
    }

    [Fact]
    public async Task HandlerMapping_DispatchesAndSuggestsOnlyTargetsForSelectedSource()
    {
        var output = new CapturingOutput();
        var command = new FormatCommand(output,
        [
            new TestHandler("wi", "branch"),
            new TestHandler("wi", "text"),
            new TestHandler("json", "text")
        ]);
        var registry = new CommandRegistry([command]);
        await registry.ExecuteAsync("format JSON TEXT hello world", CancellationToken.None);
        Assert.Equal("json:text:hello world", output.Content);
        Assert.Equal(["branch", "text"],
            command.GetSuggestions(1, "", ["WI"]).Select(item => item.Value));
        Assert.Equal("text", Assert.Single(command.GetSuggestions(1, "TE", ["json"])).Value);
        Assert.Equal("wi", Assert.Single(command.GetSuggestions(0, "WI")).Value);
        Assert.Empty(command.GetSuggestions(1, "", ["unknown"]));
        Assert.Empty(command.GetSuggestions(2, "", ["wi", "branch"]));
        await Assert.ThrowsAsync<CommandInputException>(() =>
            registry.ExecuteAsync("format json branch hello", CancellationToken.None));
    }
}
