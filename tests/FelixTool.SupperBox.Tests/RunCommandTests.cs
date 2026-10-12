using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FelixTool.SupperBox.Tests;

public sealed class RunCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"SupperBox Run-{Guid.NewGuid():N}");

    public RunCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredCommandAndWorkingDirectoryIgnoringTargetCase()
    {
        const string script = """npm run "my task" && echo "finished" | findstr finished""";
        var launcher = new RecordingRunLauncher();
        var command = new RunCommand(launcher);
        Apply(command, JsonSerializer.Serialize(new
        {
            foo = new { workingDir = _directory, command = script }
        }));

        var result = await new CommandRegistry([command]).ExecuteAsync("run FOO", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(script, launcher.Command);
        Assert.Equal(_directory, launcher.WorkingDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsToCurrentUserProfileDirectory()
    {
        var launcher = new RecordingRunLauncher();
        var command = new RunCommand(launcher);
        Apply(command, """{"foo":{"command":"dotnet --info"}}""");

        await new CommandRegistry([command]).ExecuteAsync("run foo", CancellationToken.None);

        Assert.Equal("dotnet --info", launcher.Command);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            launcher.WorkingDirectory);
    }

    [Fact]
    public void GetSuggestions_FiltersTargetNamesAndUsesCommandAsDescription()
    {
        var command = new RunCommand(new RecordingRunLauncher());
        Apply(command, """{"foo":{"command":"echo foo"},"bar":{"command":"echo bar"}}""");

        var suggestion = Assert.Single(command.GetSuggestions(0, "FO"));

        Assert.Equal("foo", suggestion.Value);
        Assert.Equal("echo foo", suggestion.Description);
        Assert.Empty(command.GetSuggestions(1, ""));
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"foo":null}""")]
    [InlineData("""{"foo":"echo hello"}""")]
    [InlineData("""{"foo":{}}""")]
    [InlineData("""{"foo":{"command":null}}""")]
    [InlineData("""{"foo":{"command":123}}""")]
    [InlineData("""{"foo":{"command":" "}}""")]
    [InlineData("""{"":{"command":"echo hello"}}""")]
    [InlineData("""{"foo":{"command":"echo hello","workingDir":null}}""")]
    [InlineData("""{"foo":{"command":"echo hello","workingDir":123}}""")]
    [InlineData("""{"foo":{"command":"echo hello","workingDir":""}}""")]
    [InlineData("""{"foo":{"command":"echo hello","workingDir":"relative"}}""")]
    [InlineData("""{"foo":{"command":"echo one"},"FOO":{"command":"echo two"}}""")]
    public void ApplyConfiguration_RejectsInvalidEntriesAndPreservesPreviousConfiguration(string json)
    {
        var command = new RunCommand(new RecordingRunLauncher());
        Apply(command, """{"previous":{"command":"echo previous"}}""");

        Assert.Throws<InvalidDataException>(() => Apply(command, json));
        Assert.Equal("previous", Assert.Single(command.GetSuggestions(0, "")).Value);
    }

    [Theory]
    [InlineData("run missing")]
    [InlineData("run")]
    [InlineData("run foo extra")]
    public async Task ExecuteAsync_RejectsUnknownTargetsAndInvalidArguments(string input)
    {
        var launcher = new RecordingRunLauncher();
        var command = new RunCommand(launcher);
        Apply(command, """{"foo":{"command":"echo hello"}}""");

        await Assert.ThrowsAsync<CommandInputException>(() =>
            new CommandRegistry([command]).ExecuteAsync(input, CancellationToken.None));

        Assert.Null(launcher.Command);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingWorkingDirectory()
    {
        var launcher = new RecordingRunLauncher();
        var command = new RunCommand(launcher);
        Apply(command, JsonSerializer.Serialize(new
        {
            foo = new { command = "echo hello", workingDir = Path.Combine(_directory, "Missing") }
        }));

        var exception = await Assert.ThrowsAsync<CommandInputException>(() =>
            new CommandRegistry([command]).ExecuteAsync("run foo", CancellationToken.None));

        Assert.Contains("工作目录不存在", exception.Message);
        Assert.Null(launcher.Command);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCancellationBeforeLaunching()
    {
        var launcher = new RecordingRunLauncher();
        var command = new RunCommand(launcher);
        Apply(command, """{"foo":{"command":"echo hello"}}""");

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new CommandRegistry([command]).ExecuteAsync("run foo", new CancellationToken(true)));

        Assert.Null(launcher.Command);
    }

    [Fact]
    public void Launcher_CreatesCmdWindowThatRemainsOpenAndPreservesCommandQuotes()
    {
        const string command = """npm run "my task" && dotnet test "C:\some path\app.csproj" """;

        var startInfo = RunCommandLauncher.CreateStartInfo(command, _directory);

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Equal($"/d /s /k \"{command}\"", startInfo.Arguments);
        Assert.Equal(_directory, startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public async Task Launcher_ExecutesQuotedCmdScriptWithChainingPipesAndWorkingDirectory()
    {
        var scriptPath = Path.Combine(_directory, "sample script.cmd");
        File.WriteAllText(scriptPath, "@echo off\r\necho %CD%\r\necho [%~1]\r\nexit /b 0\r\n");
        var startInfo = RunCommandLauncher.CreateStartInfo(
            $"\"{scriptPath}\" \"two words\" && echo completed | findstr completed & exit 0",
            _directory);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;
        using var process = Process.Start(startInfo)!;
        try
        {
            process.StandardInput.Close();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var output = await outputTask;
            var error = await errorTask;

            Assert.Equal(0, process.ExitCode);
            Assert.Contains(_directory, output);
            Assert.Contains("[two words]", output);
            Assert.Contains("completed", output);
            Assert.True(string.IsNullOrWhiteSpace(error), error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public async Task SystemConfiguration_BindsCommandsSectionAndReloadsTargets()
    {
        var configurationDirectory = Path.Combine(_directory, "FelixTool.SupperBox");
        Directory.CreateDirectory(configurationDirectory);
        var path = Path.Combine(configurationDirectory, "config.json");
        File.WriteAllText(path, """{"commands":{"foo":{"command":"echo foo"}}}""");
        using var configuration = new SystemConfiguration(_directory);
        var command = new RunCommand(new RecordingRunLauncher());
        configuration.Bind([command]);
        Assert.Equal("foo", Assert.Single(command.GetSuggestions(0, "")).Value);

        var reloaded = new TaskCompletionSource<ConfigurationReloadedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        configuration.Reloaded += (_, args) => reloaded.TrySetResult(args);
        File.WriteAllText(path, """{"commands":{"bar":{"command":"echo bar"}}}""");

        var result = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.Message);
        Assert.Equal("bar", Assert.Single(command.GetSuggestions(0, "")).Value);
    }

    [Fact]
    public void SystemConfiguration_OlderConfigWithoutCommandsSectionRemainsCompatible()
    {
        var configurationDirectory = Path.Combine(_directory, "FelixTool.SupperBox");
        Directory.CreateDirectory(configurationDirectory);
        File.WriteAllText(Path.Combine(configurationDirectory, "config.json"), "{}");
        using var configuration = new SystemConfiguration(_directory);
        var command = new RunCommand(new RecordingRunLauncher());

        configuration.Bind([command]);

        Assert.Empty(command.GetSuggestions(0, ""));
    }

    [Fact]
    public void SystemConfiguration_NewConfigIncludesEmptyCommandsSection()
    {
        using var configuration = new SystemConfiguration(_directory);
        using var document = JsonDocument.Parse(File.ReadAllText(configuration.FilePath));

        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("commands").ValueKind);
        Assert.Empty(document.RootElement.GetProperty("commands").EnumerateObject());
    }

    private static void Apply(RunCommand command, string json)
    {
        using var document = JsonDocument.Parse(json);
        command.ApplyConfiguration(document.RootElement);
    }

    private sealed class RecordingRunLauncher : IRunCommandLauncher
    {
        public string? Command { get; private set; }
        public string? WorkingDirectory { get; private set; }

        public void Run(string command, string workingDirectory)
        {
            Command = command;
            WorkingDirectory = workingDirectory;
        }
    }
}
